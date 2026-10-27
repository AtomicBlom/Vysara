using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using RoseMcp.Tap.Endpoint;
using RoseMcp.Tap.Protocol;

namespace SUIsei;

/// <summary>
/// What a diagnostics tool loaded into a running game builds on: the live hosts and their element trees, each
/// dependency property's value and the level it came from, picking and bounds in surface pixels, a top-most overlay for
/// the tool's own UI, run-time value parsing, and where each element came from in markup.
/// <para>
/// Everything here is behind one feature switch, <c>SUIsei.Diagnostics.Enabled</c>. It is off unless the game opts in
/// through its runtime config, typically in a Debug configuration:
/// <c>&lt;RuntimeHostConfigurationOption Include="SUIsei.Diagnostics.Enabled" Value="true" Trim="true" /&gt;</c>.
/// It is always off where dynamic code is not supported, as under NativeAOT, and where it is off the trimmer removes
/// the code it guards. A member called while it is off throws <see cref="InvalidOperationException"/>, except the two
/// that generated code and engine glue call without asking first, <c>RecordSource</c> and
/// <see cref="SetFrameSource"/>, which then do nothing.
/// </para>
/// <para>
/// An element tree belongs to the thread that ticks its host. A tool on another thread reaches it through
/// <see cref="Post"/>, and calls every member that reads or changes an element from the work it posts.
/// </para>
/// </summary>
public static partial class Diagnostics
{
	private const string SwitchName = "SUIsei.Diagnostics.Enabled";

	// The switch is fixed for the life of the process, so it is read once.
	private static readonly bool SwitchOn = AppContext.TryGetSwitch(SwitchName, out var on) && on;

	// Work posted for the next tick; made on the first post, so a game nobody inspects holds no queue.
	private static ConcurrentQueue<Action>? _posted;

	/// <summary>
	/// Whether diagnostics are on: the runtime config sets <c>SUIsei.Diagnostics.Enabled</c> to true and dynamic code
	/// is supported. Code inside <c>if (Diagnostics.Enabled)</c> may load and reflect over assemblies without the trim
	/// and AOT analyzers objecting, and is removed where the switch is off.
	/// </summary>
	[FeatureSwitchDefinition(SwitchName)]
	[FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
	[FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
	[UnconditionalSuppressMessage("Trimming", "IL4000",
		Justification = "The analyzer accepts only a bare feature check as a guard's body, and this one is also a switch. It is "
			+ "false wherever dynamic code is not, which covers NativeAOT; a trimmed app that sets the switch has asked for "
			+ "the code it guards, and its trimmer substitutes the value it set.")]
	public static bool Enabled => RuntimeFeature.IsDynamicCodeSupported && SwitchOn;

	/// <summary>The live hosts, in the order they were built.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static IReadOnlyList<UIHost> Hosts
	{
		get
		{
			RequireEnabled();
			return HotReload.LiveHosts();
		}
	}

	/// <summary>
	/// Run <paramref name="action"/> at the start of the next <see cref="UIHost.Tick"/> of any host, on the thread that
	/// ticks it, before that tick's layout: a change it makes lays out and draws that frame, and what it reads is what
	/// the last frame drew. Safe to call from any thread. An exception the action throws leaves that tick, so an action
	/// catches its own; work posted after it runs on the next tick. Nothing is allocated while nothing is posted.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static void Post(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);
		RequireEnabled();
		var queue = Volatile.Read(ref _posted);
		if (queue is null)
		{
			Interlocked.CompareExchange(ref _posted, new ConcurrentQueue<Action>(), null);
			queue = _posted!;
		}
		queue.Enqueue(action);
	}

	// Called by UIHost.Tick. Runs only what was queued when it started, so an action that posts again waits a tick.
	internal static void RunPosted()
	{
		var queue = Volatile.Read(ref _posted);
		if (queue is null || queue.IsEmpty)
			return;
		for (var remaining = queue.Count; remaining > 0 && queue.TryDequeue(out var action); remaining--)
			action();
	}

	// The pipe the diagnostics endpoint listens on, as a tap's RoseMcpTap attribute names it.
	private const string EndpointPipe = "suisei-diag-{pid}";

	// The endpoint a RoseMCP host loads a tap through: started with the first host and kept for the life of the process.
	private static TapEndpoint? _endpoint;
	private static int _endpointStarting;

	/// <summary>
	/// Start the diagnostics endpoint on <c>suisei-diag-{pid}</c>, once per process, so a RoseMCP host can load a tap into
	/// this game whether it launched it or attached to it later. Called as each <see cref="UIHost"/> is built; does
	/// nothing while diagnostics are off. What the endpoint does goes to <see cref="Trace"/>.
	/// </summary>
	internal static void StartEndpoint()
	{
		if (!Enabled || Interlocked.Exchange(ref _endpointStarting, 1) != 0)
			return;
		var version = typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
		_endpoint = TapEndpoint.Start(EndpointWire.PipeName(EndpointPipe, Environment.ProcessId), "SUIsei", version,
			static message => Trace.WriteLine(message, "SUIsei diagnostics"));
	}

	/// <summary>The elements directly beneath <paramref name="element"/> in the tree that lays out and draws: a panel's
	/// children, a content control's content, a templated control's template root.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static IReadOnlyList<UIElement> ChildrenOf(UIElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireEnabled();
		return element.VisualChildren;
	}

	/// <summary>The elements at the top of <paramref name="view"/>'s tree: its root content, then any open popups and
	/// flyouts bottom-to-top. Overlays added with <see cref="AddOverlay"/> are left out.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static IReadOnlyList<UIElement> ChildrenOf(UIView view)
	{
		ArgumentNullException.ThrowIfNull(view);
		RequireEnabled();
		return view.Root.TopLevelElements();
	}

	/// <summary>The element <paramref name="element"/> sits beneath, or null for an element at the top of a view's tree
	/// or one in no tree.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static UIElement? ParentOf(UIElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireEnabled();
		return element.Parent;
	}

	/// <summary>
	/// The top-most element drawn at <paramref name="surfacePoint"/>, in <paramref name="view"/>'s surface pixels, as
	/// input hit-tests it: through the composition's transforms and clips, counting an element only where its content
	/// draws. Overlays added with <see cref="AddOverlay"/> are looked through. With <paramref name="includeHidden"/>,
	/// elements whose <see cref="UIElement.IsHitTestVisible"/> is false are found too. Null where nothing is drawn.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static UIElement? HitTest(UIView view, Vector2 surfacePoint, bool includeHidden)
	{
		ArgumentNullException.ThrowIfNull(view);
		RequireEnabled();
		return view.Root.HitTest(surfacePoint, includeHidden, diagnosticsPick: true);
	}

	/// <summary>
	/// <paramref name="element"/>'s layout rectangle in its view's surface pixels, through the transforms the compositor
	/// draws it with: layout placement, render transforms, animated offsets and the view's DIP-to-pixel scale. Under a
	/// rotation or skew it is the axis-aligned bounds. Null for an element in no view.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static Rect? Bounds(UIElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireEnabled();
		if (element.AttachedRoot is not { } root)
			return null;
		var world = element.TransformToRoot(out _) * root.Visual.Transform;
		return new Rect(0f, 0f, element.LayoutRect.Width, element.LayoutRect.Height).TransformBounds(world);
	}

	/// <summary>
	/// Show <paramref name="overlay"/> over all of <paramref name="view"/>'s surface, above its content and every popup,
	/// including popups that open later. It draws into the view's own surface and is hit-tested first, so its
	/// interactive parts take input before the game's UI, and it is left out of <see cref="ChildrenOf(UIView)"/> and
	/// <see cref="HitTest"/>. Remove it with <see cref="RemoveOverlay"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="overlay"/> is already in a tree.</exception>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static void AddOverlay(UIView view, UIElement overlay)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(overlay);
		RequireEnabled();
		if (overlay.Parent is not null || overlay.AttachedRoot is not null)
			throw new ArgumentException("The overlay is already in a tree; it must be an element of its own.", nameof(overlay));
		view.Root.AddDiagnosticOverlay(overlay);
	}

	/// <summary>Take down an overlay added with <see cref="AddOverlay"/>. False when <paramref name="overlay"/> is not
	/// one.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static bool RemoveOverlay(UIElement overlay)
	{
		ArgumentNullException.ThrowIfNull(overlay);
		RequireEnabled();
		if (overlay.AttachedRoot is not { } root || !root.IsDiagnosticOverlay(overlay))
			return false;
		root.RemoveOverlay(overlay);
		return true;
	}

	/// <summary>Whether <paramref name="element"/> is an overlay added with <see cref="AddOverlay"/>, which a tool
	/// leaves out of what it reports.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static bool IsOverlay(UIElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireEnabled();
		return element.AttachedRoot is { } root && root.IsDiagnosticOverlay(element);
	}

	/// <summary>
	/// Hold <paramref name="host"/>'s <see cref="UIHost.WantsPointer"/> true, from now rather than the next tick, so an
	/// input layer that reads it keeps the click a tool is about to pick with from reaching the game. Releasing it lets
	/// the next tick report the pointer as it is.
	/// </summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static void HoldPointer(UIHost host, bool hold)
	{
		ArgumentNullException.ThrowIfNull(host);
		RequireEnabled();
		host.PointerHeld = hold;
		if (hold)
			host.WantsPointer = true;
	}

	private static void RequireEnabled()
	{
		if (!Enabled)
			throw new InvalidOperationException(
				$"SUIsei diagnostics are off. A game turns them on with the runtime config property '{SwitchName}', which NativeAOT never allows.");
	}
}
