using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(SUIsei.HotReloadHandler))]

namespace SUIsei;

/// <summary>
/// XAML Hot Reload support. A generated view registers itself here in <c>InitializeComponent()</c> (a no-capture
/// <see cref="Track"/> call), and each <see cref="UIHost"/> registers itself on construction. When the runtime
/// applies a metadata delta (an edited <c>.xaml</c> re-runs the source generator under <c>dotnet watch</c>), the
/// <see cref="HotReloadHandler"/> records the changed types; the next <see cref="UIHost.Tick"/> drains them and
/// rebuilds the affected views on the UI thread — never on the hot-reload agent's thread.
/// <para>
/// Bookkeeping is gated on <see cref="Enabled"/>, which defaults to <see cref="MetadataUpdater.IsSupported"/>:
/// it is <c>false</c> under NativeAOT / optimized publish, so the whole mechanism is inert and trimmable there,
/// preserving the reflection-free runtime. Tests force it on.
/// </para>
/// </summary>
public static class HotReload
{
	// A tracked view: a weak handle to the instance plus no-capture delegates (so the entry never keeps the view
	// alive). Rebuild re-runs the generated InitializeComponent; GetRoot reads its current root element.
	private sealed record Entry(WeakReference Target, Action<object> Rebuild, Func<object, UIElement> GetRoot);

	private static readonly object Sync = new();
	private static readonly List<Entry> Components = [];
	private static readonly List<WeakReference<UIHost>> Hosts = [];
	private static readonly HashSet<Type> Pending = [];

	/// <summary>Whether hot-reload bookkeeping runs. Defaults to the runtime's metadata-update capability; set
	/// <c>true</c> in tests to exercise the rebuild path without the hot-reload agent.</summary>
	public static bool Enabled { get; set; } = MetadataUpdater.IsSupported;

	/// <summary>Register a generated view. <paramref name="rebuild"/> and <paramref name="getRoot"/> must be
	/// no-capture (static) delegates that take the instance, so the registry holds only a weak reference. Idempotent.</summary>
	public static void Track(object component, Action<object> rebuild, Func<object, UIElement> getRoot)
	{
		if (!Enabled)
			return;
		lock (Sync)
		{
			Prune();
			foreach (var c in Components)
			{
				if (c.Target.Target is { } t && ReferenceEquals(t, component))
					return;
			}
			Components.Add(new Entry(new WeakReference(component), rebuild, getRoot));
		}
	}

	internal static void TrackHost(UIHost host)
	{
		if (!Enabled)
			return;
		lock (Sync)
		{
			PruneHosts();
			Hosts.Add(new WeakReference<UIHost>(host));
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
					if (c.Target.Target is { } t)
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
	/// the ticking thread. Each affected view is rebuilt and, if it was hosted as a <see cref="UIRoot.Content"/>,
	/// its fresh root is re-hosted in place.</summary>
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
			if (c.Target.Target is not { } target)
				continue;
			if (Array.IndexOf(types, target.GetType()) < 0)
				continue;
			// Only rebuild views currently hosted as a UIRoot.Content (the standard hosting). An unhosted tracked
			// view has nothing on screen to refresh, so it is skipped rather than silently rebuilt.
			var host = FindHostingRoot(c.GetRoot(target));
			if (host is null)
				continue;
			c.Rebuild(target);
			host.Content = c.GetRoot(target);
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

	// The UIRoot currently hosting `element` directly as its Content, across all live hosts (the standard
	// `uiRoot.Content = view.Root` hosting); null when the view is not hosted that way.
	private static UIRoot? FindHostingRoot(UIElement element)
	{
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

	private static void Prune() => Components.RemoveAll(c => c.Target.Target is null);

	private static void PruneHosts() => Hosts.RemoveAll(wr => !wr.TryGetTarget(out _));
}
