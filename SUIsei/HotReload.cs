using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(SUIsei.HotReloadHandler))]

namespace SUIsei;

/// <summary>
/// XAML Hot Reload support. A generated view registers itself here in <c>InitializeComponent()</c> and each
/// <see cref="UIHost"/> registers itself on construction. When the runtime applies a metadata delta (an edited
/// <c>.xaml</c> re-runs the source generator under <c>dotnet watch</c>), the <see cref="HotReloadHandler"/>
/// records the changed types; the next <see cref="UIHost.Tick"/> drains them and rebuilds the affected views on
/// the UI thread — never on the hot-reload agent's thread.
/// <para>
/// Bookkeeping is gated on <see cref="Enabled"/>, which defaults to <see cref="MetadataUpdater.IsSupported"/>:
/// it is <c>false</c> under NativeAOT / optimized publish, so the whole mechanism is inert and trimmable there,
/// preserving the reflection-free runtime. Tests force it on.
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
	/// <c>true</c> in tests to exercise the rebuild path without the hot-reload agent.</summary>
	public static bool Enabled { get; set; } = MetadataUpdater.IsSupported;

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
	/// the ticking thread. Each affected view is rebuilt (in-place for inheritance roots; with Root reassignment for
	/// composition roots) and the hosting <see cref="UIRoot.Content"/> is re-assigned to trigger re-layout.</summary>
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

		foreach (var c in comps)
		{
			if (!c.Target.TryGetTarget(out var target))
				continue;
			if (Array.IndexOf(types, target.GetType()) < 0)
				continue;
			// Only rebuild views currently hosted as a UIRoot.Content. An unhosted tracked view has nothing on
			// screen to refresh, so it is skipped rather than silently rebuilt.
			var host = FindHostingRoot(c);
			if (host is null)
				continue;
			c.Rebuild(target);
			// For composition roots the Root field is reassigned during rebuild; retrieve the new instance.
			var newElement = c.GetRoot != null ? c.GetRoot(target) : (UIElement)target;
			host.Content = newElement;
		}
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

	// Find the UIRoot currently hosting this entry's UIElement as its Content, across all live hosts.
	private static UIRoot? FindHostingRoot(Entry entry)
	{
		if (!entry.Target.TryGetTarget(out var target))
			return null;
		var element = entry.GetRoot != null ? entry.GetRoot(target) : (UIElement)target;
		lock (Sync)
		{
			PruneHosts();
			foreach (var wr in Hosts)
			{
				if (!wr.TryGetTarget(out var host))
					continue;
				foreach (var view in host.Views)
				{
					if (ReferenceEquals(view.Root.Content, element))
						return view.Root;
				}
			}
		}
		return null;
	}

	private static void Prune() => Components.RemoveAll(c => !c.Target.TryGetTarget(out _));

	private static void PruneHosts() => Hosts.RemoveAll(wr => !wr.TryGetTarget(out _));
}
