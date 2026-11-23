using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using BinaryVibrance.Vysara.Controls;

[assembly: MetadataUpdateHandler(typeof(BinaryVibrance.Vysara.HotReloadHandler))]

namespace BinaryVibrance.Vysara;

/// <summary>
/// XAML Hot Reload support. A generated view registers itself here in <c>InitializeComponent()</c> and each
/// <see cref="UIHost"/> registers itself on construction. When the runtime applies a metadata delta (an edited
/// <c>.xaml</c> re-runs the source generator under <c>dotnet watch</c>), the <see cref="HotReloadHandler"/>
/// records the changed types; the next <see cref="UIHost.Tick"/> drains them and rebuilds the affected views on
/// the UI thread — never on the hot-reload agent's thread. A view rebuilds wherever it sits in a live host's tree, nested
/// in another view as much as hosted as a <see cref="UIRoot.Content"/>, and its code-behind hears of it through the
/// generated <c>partial void OnComponentRebuilt()</c>.
/// <para>
/// Bookkeeping is gated on <see cref="Enabled"/>, which defaults to <see cref="MetadataUpdater.IsSupported"/> and is
/// always <c>false</c> where dynamic code is not supported: under NativeAOT it reads as a constant false, so the trimmer
/// removes the code it guards, preserving the reflection-free runtime. Tests force it on.
/// </para>
/// </summary>
public static class HotReload
{
	// A tracked view: a weak handle to the component object, a no-capture rebuild delegate, and an optional
	// getRoot that maps the component to its UIElement root (null when the component IS the root).
	// Stored type-erased so the list is homogeneous.
	private sealed record Entry(WeakReference<object> Target, Action<object> Rebuild, Func<object, UIElement>? GetRoot);

	private static readonly object Sync = new();
	private static readonly List<Entry> Components = [];
	private static readonly List<WeakReference<UIHost>> Hosts = [];
	private static readonly HashSet<Type> Pending = [];

	/// <summary>Whether hot-reload bookkeeping runs. Defaults to the runtime's metadata-update capability; set
	/// <c>true</c> in tests to exercise the rebuild path without the hot-reload agent. Always false where dynamic code is
	/// not supported, whatever it was set to: <see cref="RuntimeFeature.IsDynamicCodeSupported"/> is a constant the trimmer
	/// folds, so under NativeAOT the getter is false and what it guards is removed.</summary>
	public static bool Enabled { get => RuntimeFeature.IsDynamicCodeSupported && field; set; } = MetadataUpdater.IsSupported;

	/// <summary>Register a generated view where the component IS the root UIElement (non-sealed root type, e.g.
	/// ContentControl). <paramref name="rebuild"/> must be a no-capture static delegate. Idempotent.</summary>
	public static void Track<T>(T component, Action<T> rebuild) where T : UIElement
	{
		if (!Enabled)
			return;
		lock (Sync)
		{
			Prune();
			foreach (var c in Components)
			{
				if (c.Target.TryGetTarget(out var t) && ReferenceEquals(t, component))
					return;
			}
			Components.Add(new Entry(new WeakReference<object>(component), o => rebuild((T)o), null));
		}
	}

	/// <summary>Register a generated view where the component wraps a separate UIElement root (sealed root type,
	/// e.g. Border or StackPanel). <paramref name="rebuild"/> rebuilds the component in-place (reassigning its
	/// Root field); <paramref name="getRoot"/> returns the current root UIElement after rebuild. Idempotent.</summary>
	public static void Track(object component, Action<object> rebuild, Func<object, UIElement> getRoot)
	{
		if (!Enabled)
			return;
		lock (Sync)
		{
			Prune();
			foreach (var c in Components)
			{
				if (c.Target.TryGetTarget(out var t) && ReferenceEquals(t, component))
					return;
			}
			Components.Add(new Entry(new WeakReference<object>(component), rebuild, getRoot));
		}
	}

	// The host registry serves diagnostics as well as hot reload, so it is kept while either is on.
	internal static void TrackHost(UIHost host)
	{
		if (!Enabled && !Diagnostics.Enabled)
			return;
		lock (Sync)
		{
			PruneHosts();
			Hosts.Add(new WeakReference<UIHost>(host));
		}
	}

	/// <summary>The hosts still alive, in the order they were built; empty unless hot reload or diagnostics are on.</summary>
	internal static UIHost[] LiveHosts()
	{
		lock (Sync)
		{
			PruneHosts();
			var hosts = new List<UIHost>(Hosts.Count);
			foreach (var wr in Hosts)
			{
				if (wr.TryGetTarget(out var host))
					hosts.Add(host);
			}
			return [.. hosts];
		}
	}

	/// <summary>Record types whose metadata just changed. A <c>null</c> array means "everything" (the runtime's
	/// convention), so every tracked view is queued. The rebuild itself is deferred to <see cref="ProcessPending"/>.</summary>
	public static void NotifyUpdate(Type[]? updatedTypes)
	{
		if (!Enabled)
			return;
		lock (Sync)
		{
			if (updatedTypes is null)
			{
				foreach (var c in Components)
				{
					if (c.Target.TryGetTarget(out var t))
						Pending.Add(t.GetType());
				}
			}
			else
			{
				foreach (var t in updatedTypes)
					Pending.Add(t);
			}
		}
	}

	/// <summary>Drain queued rebuilds. Called at the top of <see cref="UIHost.Tick"/> so reconstruction happens on
	/// the ticking thread. Each affected view in a live host's tree is rebuilt, wherever it sits in that tree: in place
	/// for inheritance roots, and for composition roots with the new Root put where the old one was
	/// (<see cref="ReplaceRoot"/>). Outer views rebuild before the views nested in them.</summary>
	public static void ProcessPending()
	{
		if (!Enabled)
			return;
		Type[] types;
		Entry[] comps;
		lock (Sync)
		{
			if (Pending.Count == 0)
				return;
			types = [.. Pending];
			Pending.Clear();
			Prune();
			comps = [.. Components];
		}

		// Outermost first: an outer view's rebuild makes new instances of the views nested in its XAML, so an inner
		// view rebuilt before it would be rebuilt for nothing.
		var queue = new List<(Entry Entry, object Target, int Depth)>();
		foreach (var c in comps)
		{
			if (c.Target.TryGetTarget(out var target) && Array.IndexOf(types, target.GetType()) >= 0)
				queue.Add((c, target, Depth(RootOf(c, target))));
		}

		foreach (var (c, target, _) in queue.OrderBy(q => q.Depth))
		{
			// Decided now rather than up front: a view an earlier rebuild replaced has left the live tree. An unhosted
			// view has nothing on screen to refresh, so it is skipped rather than silently rebuilt.
			var element = RootOf(c, target);
			if (!IsLive(element))
				continue;
			// A composition view's new Root takes the old one's place, so the old one must be somewhere that can take it.
			if (c.GetRoot is not null && FindSlot(element) is null)
				continue;
			c.Rebuild(target);
		}
	}

	/// <summary>
	/// Put a rebuilt composition view's new root where its old root was: at the same index in a <see cref="Panel"/>'s
	/// children, as a <see cref="Border"/>'s child, as a <see cref="ScrollViewer"/>'s, a <see cref="ContentControl"/>'s (a
	/// templated one's too, whose presenter shows it) or a <see cref="ContentPresenter"/>'s content, or as a
	/// <see cref="UIRoot"/>'s content. The generated
	/// <c>RebuildComponent()</c> calls it once the new tree is built, before <c>OnComponentRebuilt</c>. It does nothing
	/// when the old root is anywhere else: unparented, a popup's child, or an item of an items control.
	/// </summary>
	/// <returns>Whether <paramref name="newRoot"/> took the old root's place.</returns>
	public static bool ReplaceRoot(UIElement? oldRoot, UIElement newRoot)
	{
		if (oldRoot is null || ReferenceEquals(oldRoot, newRoot) || FindSlot(oldRoot) is not { } slot)
			return false;
		switch (slot.Owner)
		{
			case UIRoot root:
				root.Content = newRoot;
				break;
			case Panel panel:
				panel.Children.RemoveAt(slot.Index);
				panel.Children.Insert(slot.Index, newRoot);
				break;
			case Border border:
				border.Child = newRoot;
				break;
			case ScrollViewer scrollViewer:
				scrollViewer.Content = newRoot;
				break;
			case ContentControl control:
				control.Content = newRoot;
				break;
			case ContentPresenter presenter:
				presenter.Content = newRoot;
				break;
		}
		return true;
	}

	/// <summary>Clear all registrations and pending work. For test isolation — the registry is process-global.</summary>
	internal static void Reset()
	{
		lock (Sync)
		{
			Components.Clear();
			Hosts.Clear();
			Pending.Clear();
		}
	}

	// The element a tracked view shows: the component itself for an inheritance root, else its Root.
	private static UIElement RootOf(Entry entry, object target) => entry.GetRoot?.Invoke(target) ?? (UIElement)target;

	// How many parents the element has above it.
	private static int Depth(UIElement element)
	{
		var depth = 0;
		for (var e = element.Parent; e is not null; e = e.Parent)
			depth++;
		return depth;
	}

	// Whether the element is in a live host's tree, at any depth: under a UIRoot's Content, or under an overlay (a
	// popup's child).
	private static bool IsLive(UIElement element)
	{
		var top = element;
		while (top.Parent is { } parent)
			top = parent;
		lock (Sync)
		{
			PruneHosts();
			foreach (var wr in Hosts)
			{
				if (!wr.TryGetTarget(out var host))
					continue;
				foreach (var view in host.Views)
				{
					if (ReferenceEquals(view.Root.Content, top))
						return true;
					foreach (var (overlay, _, _) in view.Root.Overlays)
					{
						if (ReferenceEquals(overlay, top))
							return true;
					}
				}
			}
		}
		return false;
	}

	// Where an element sits, as a place another element can take: Owner is the UIRoot, Panel, Border, ScrollViewer,
	// ContentControl or ContentPresenter holding it, and Index its place in a Panel's children.
	private readonly record struct Slot(object Owner, int Index);

	private static Slot? FindSlot(UIElement element)
	{
		switch (element.Parent)
		{
			case null:
				// A popup's child is an overlay of the root, which no popup can be found from: it has no slot.
				return element.AttachedRoot is { } root && ReferenceEquals(root.Content, element) ? new Slot(root, 0) : null;
			case Panel panel when !IsItemsHost(panel):
				var index = panel.Children.Items.IndexOf(element);
				return index >= 0 ? new Slot(panel, index) : null;
			case Border border when ReferenceEquals(border.Child, element):
				return new Slot(border, 0);
			case ScrollViewer scrollViewer when ReferenceEquals(scrollViewer.Content, element):
				return new Slot(scrollViewer, 0);
			case ContentControl control when ReferenceEquals(control.Content, element):
				return control is ListViewItem ? null : new Slot(control, 0);
			case ContentPresenter presenter:
				// A templated control shows its content through a presenter in its template: the control's Content is the
				// slot, which the template binding passes on to the presenter. A presenter given the element itself is the slot.
				for (var e = presenter.Parent; e is not null; e = e.Parent)
				{
					if (e is ContentControl templated && ReferenceEquals(templated.Content, element))
						return templated is ListViewItem ? null : new Slot(templated, 0);
				}
				return ReferenceEquals(presenter.Content, element) ? new Slot(presenter, 0) : null;
			default:
				return null;
		}
	}

	// An items control's panel and its ListViewItems hold the containers made from its items, which would only put the
	// old element back: neither is a slot.
	private static bool IsItemsHost(Panel panel) => panel.Parent is ItemsControl items && ReferenceEquals(items.ItemsHost, panel);

	private static void Prune() => Components.RemoveAll(c => !c.Target.TryGetTarget(out _));

	private static void PruneHosts() => Hosts.RemoveAll(wr => !wr.TryGetTarget(out _));
}
