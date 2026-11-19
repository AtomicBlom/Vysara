using System.Numerics;
using System.Text;
using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei;

/// <summary>
/// Per-host pointer routing: hit-tests each queued <see cref="PointerInput"/> over the
/// host's interactive overlay views, raises tunnel-then-bubble pointer routed events, tracks the "over" chain (so
/// enter/exit fire per element), implicitly captures on press so a press→release pair routes to the same target
/// (the basis for <c>Button.Click</c>), and supports explicit drag capture via
/// <see cref="RequestCapture"/>/<see cref="RequestRelease"/>. Persistent host state; headless (no engine/backend).
/// </summary>
public sealed class InputManager
{
	private readonly Dictionary<uint, List<UIElement>> _overChains = [];
	private readonly Dictionary<uint, UIElement?> _implicitCapture = [];
	private readonly Dictionary<uint, UIElement> _explicitCapture = [];
	private readonly PointerRoutedEventArgs _args = new();
	private readonly RoutedEventArgs _captureArgs = new();
	private List<UIElement> _scratch = [];
	// Each pointer's last routed event, which the exit raised on an element that left the tree under it carries.
	private readonly Dictionary<uint, PointerInput> _lastInput = [];
	// Reused by ReleaseWithin to collect the pointers whose captee left, so their entries can be removed after the walk.
	private readonly List<uint> _pointerIds = [];
	// Explicit captees and over-chain members that left the tree, whose PointerCaptureLost and PointerExited RaiseReleased
	// has yet to raise.
	private readonly List<(UIElement Element, uint PointerId)> _lostCapture = [];
	private readonly List<(UIElement Element, uint PointerId)> _exited = [];
	// A wheel gesture's target, kept while the pointer stays where the gesture began: a trackpad's two fingers or a turned
	// wheel go on scrolling what they started on as its content slides under the pointer, rather than passing to whatever
	// element (an inner viewer, a slider) arrives there. Moving the pointer, or pressing, ends the gesture.
	private (UIElement Target, uint PointerId, Vector2 Position)? _wheelLatch;
	private const float WheelLatchSlop = 2f;

	/// <summary>Invoked by <see cref="UIHost"/> on every pointer press so it can update keyboard focus.</summary>
	internal Action<UIElement?>? OnPress { get; set; }

	/// <summary>Invoked by <see cref="UIHost"/> on every wheel turn, which hides a tooltip.</summary>
	internal Action? OnWheel { get; set; }

	// The mouse or pen that moved last, whose leaf a tooltip follows; a touch never hovers.
	private uint? _hoverPointer;

	/// <summary>Route one pointer event over <paramref name="views"/> (called by <see cref="UIHost.Tick"/>).</summary>
	internal void Process(in PointerInput e, IReadOnlyList<UIView> views)
	{
		var hit = HitTest(e.Position, views);
		_lastInput[e.PointerId] = e;
		if (e.Device != PointerDeviceType.Touch)
			_hoverPointer = e.PointerId;

		// A wheel event is its own routed event (no over-chain / move semantics): route it to the captee or the
		// element under the pointer and stop.
		if (e.Wheel != Vector2.Zero)
		{
			var wheelTarget = GetCapture(e.PointerId) ?? LatchedWheelTarget(e) ?? hit;
			OnWheel?.Invoke();
			if (wheelTarget is not null)
				RaiseWheel(wheelTarget, e);
			_wheelLatch = wheelTarget is null ? null : (wheelTarget, e.PointerId, e.Position);
			return;
		}
		if (_wheelLatch is { } latch && (latch.PointerId != e.PointerId || e.Update != PointerUpdateKind.None
			|| Vector2.DistanceSquared(latch.Position, e.Position) > WheelLatchSlop * WheelLatchSlop))
			_wheelLatch = null;

		UpdateOverChain(e, hit);

		// Explicit capture wins over implicit; both win over hit for move/release routing.
		var captee = GetCapture(e.PointerId);

		if (IsPress(e.Update))
		{
			_implicitCapture[e.PointerId] = hit;
			OnPress?.Invoke(hit);
			if (hit is not null)
				Raise(UIElement.PointerPressedEvent, hit, e);
		}
		else if (IsRelease(e.Update))
		{
			var target = captee ?? hit;
			if (target is not null)
				Raise(UIElement.PointerReleasedEvent, target, e);
			_implicitCapture.Remove(e.PointerId);
			// A touch or pen lifted is gone, not hovering: it leaves whatever it was over (as WinUI's do), so nothing stays
			// lit by it and WantsPointer doesn't hold for a contact that will never move again
			if (e.Device != PointerDeviceType.Mouse)
				Leave(e);
		}
		else
		{
			var target = captee ?? hit;
			if (target is not null)
				Raise(UIElement.PointerMovedEvent, target, e);
		}
	}

	/// <summary>
	/// The innermost element the mouse or pen that moved last is over, and where it is (surface pixels); null when it is
	/// over nothing, held down on something (a press is not a rest), or there has been no such pointer.
	/// </summary>
	internal UIElement? HoverLeaf(out Vector2 position)
	{
		position = default;
		if (_hoverPointer is not { } id || !_overChains.TryGetValue(id, out var chain) || chain.Count == 0)
			return null;
		var last = _lastInput.GetValueOrDefault(id);
		if (last.Buttons != PointerButtons.None)
			return null;
		position = last.Position;
		return chain[0];
	}

	/// <summary>The element currently captured for <paramref name="pointerId"/> — explicit if set, else implicit
	/// press-capture.</summary>
	public UIElement? GetCapture(uint pointerId) =>
		_explicitCapture.GetValueOrDefault(pointerId) ?? _implicitCapture.GetValueOrDefault(pointerId);

	/// <summary>
	/// True when the host would route pointer input to UI content this frame: a pointer sits over drawn UI content
	/// (a non-empty over-chain — hit-testing lands only where an element actually draws, so transparent panels fall
	/// through) or a capture is active — explicit drag-capture, or implicit press-capture that landed on an element.
	/// The pointer half of the coarse capture report: it stays true through a drag that leaves the pressed element,
	/// exactly as press-capture keeps routing the release back to that element, and it is false for a press that
	/// landed on empty space (a null implicit captee). The over-chain reflects the last pointer event's position and
	/// is not cleared on release, so a pointer that stops sending events (a touch/pen after lift, or a mouse that left
	/// the window without a final move) keeps this true until a later event lands the pointer off the content.
	/// </summary>
	internal bool WantsPointer
	{
		get
		{
			foreach (var chain in _overChains.Values)
			{
				if (chain.Count > 0)
					return true;
			}

			if (_explicitCapture.Count > 0)
				return true;

			foreach (var captee in _implicitCapture.Values)
			{
				if (captee is not null)
					return true;
			}

			return false;
		}
	}

	/// <summary>Grant explicit capture of <paramref name="pointerId"/> to <paramref name="element"/>. If another
	/// element already holds explicit capture for this pointer, <see cref="UIElement.PointerCaptureLostEvent"/> is
	/// raised on it first. Returns true always (the element is now the captee).</summary>
	internal bool RequestCapture(uint pointerId, UIElement element)
	{
		if (_explicitCapture.TryGetValue(pointerId, out var prev) && !ReferenceEquals(prev, element))
			RaiseCaptureLost(prev);
		_explicitCapture[pointerId] = element;
		return true;
	}

	/// <summary>Release explicit capture of <paramref name="pointerId"/> if <paramref name="element"/> currently
	/// holds it; raises <see cref="UIElement.PointerCaptureLostEvent"/> on the element. Returns false if
	/// <paramref name="element"/> is not the current explicit captee.</summary>
	internal bool RequestRelease(uint pointerId, UIElement element)
	{
		if (!_explicitCapture.TryGetValue(pointerId, out var cap) || !ReferenceEquals(cap, element))
			return false;
		_explicitCapture.Remove(pointerId);
		RaiseCaptureLost(element);
		return true;
	}

	/// <summary>
	/// <paramref name="subtree"/> left the live tree: release every capture an element in it holds and drop its elements
	/// from the pointers' over-chains, so a detached element neither keeps receiving a drag nor keeps the host reporting
	/// that it wants the pointer. <see cref="UIElement.PointerCaptureLostEvent"/> on an explicit captee and
	/// <see cref="UIElement.PointerExitedEvent"/> on each element dropped from an over-chain (which clears a button's
	/// PointerOver) are queued for <see cref="RaiseReleased"/>, which the host calls once layout is done: an element can
	/// leave in the middle of a layout pass (a virtualizing panel recycling a row), and no app code runs there. Runs as
	/// elements leave, which includes every container a virtualizing panel recycles, so it allocates nothing once warm.
	/// </summary>
	internal void ReleaseWithin(UIElement subtree)
	{
		if (_wheelLatch is { } latch && latch.Target.IsWithin(subtree))
			_wheelLatch = null;

		_pointerIds.Clear();
		foreach (var (pointerId, captee) in _explicitCapture)
		{
			if (captee.IsWithin(subtree))
				_pointerIds.Add(pointerId);
		}
		foreach (var pointerId in _pointerIds)
		{
			if (_explicitCapture.Remove(pointerId, out var captee))
				_lostCapture.Add((captee, pointerId));
		}

		_pointerIds.Clear();
		foreach (var (pointerId, captee) in _implicitCapture)
		{
			if (captee is not null && captee.IsWithin(subtree))
				_pointerIds.Add(pointerId);
		}
		foreach (var pointerId in _pointerIds)
			_implicitCapture.Remove(pointerId);

		// In chain order, leaf first, as a move off them would raise their exits.
		foreach (var (pointerId, chain) in _overChains)
		{
			for (var i = 0; i < chain.Count;)
			{
				if (chain[i].IsWithin(subtree))
				{
					_exited.Add((chain[i], pointerId));
					chain.RemoveAt(i);
				}
				else
					i++;
			}
		}
	}

	/// <summary>
	/// Raise what <see cref="ReleaseWithin"/> queued: <see cref="UIElement.PointerCaptureLostEvent"/> on each explicit
	/// captee that left, unless it has captured that pointer again since, then <see cref="UIElement.PointerExitedEvent"/>
	/// on each element dropped from an over-chain, unless the pointer is over it again. Called by the host outside layout.
	/// A handler may change capture or make more elements leave; what that queues is raised in the same call.
	/// </summary>
	internal void RaiseReleased()
	{
		for (var i = 0; i < _lostCapture.Count; i++)
		{
			var (element, pointerId) = _lostCapture[i];
			if (_explicitCapture.TryGetValue(pointerId, out var captee) && ReferenceEquals(captee, element))
				continue;
			RaiseCaptureLost(element);
		}
		_lostCapture.Clear();

		for (var i = 0; i < _exited.Count; i++)
		{
			var (element, pointerId) = _exited[i];
			if (_overChains.TryGetValue(pointerId, out var chain) && Contains(chain, element))
				continue;
			RaiseDirect(UIElement.PointerExitedEvent, element, _lastInput.GetValueOrDefault(pointerId));
		}
		_exited.Clear();
	}

	// Diff the new over-chain against the previous one and raise enter/exit (direct, per element). The previous
	// chain is reused as the next scratch buffer to avoid per-move allocation.
	private void UpdateOverChain(in PointerInput e, UIElement? hit)
	{
		_scratch.Clear();
		for (UIElement? el = hit; el is not null; el = el.Parent)
			_scratch.Add(el);

		var prev = _overChains.GetValueOrDefault(e.PointerId);

		if (prev is not null)
		{
			foreach (var el in prev)
			{
				if (!Contains(_scratch, el))
					RaiseDirect(UIElement.PointerExitedEvent, el, e);
			}
		}

		foreach (var el in _scratch)
		{
			if (prev is null || !Contains(prev, el))
				RaiseDirect(UIElement.PointerEnteredEvent, el, e);
		}

		_overChains[e.PointerId] = _scratch;
		_scratch = prev ?? [];
	}

	// A pointer gone (a touch or pen lifted): exits raised on everything it was over, innermost first, and its chain forgotten.
	private void Leave(in PointerInput e)
	{
		if (!_overChains.Remove(e.PointerId, out var chain))
			return;
		foreach (var el in chain)
			RaiseDirect(UIElement.PointerExitedEvent, el, e);
	}

	// Raise a bubble event preceded by its tunnel Preview counterpart. If the Preview is handled, the bubble is skipped.
	private void Raise(RoutedEvent evt, UIElement target, in PointerInput e)
	{
		_args.Pointer = new PointerPoint(e.Position, e.Buttons, e.Pressure);
		_args.Device = e.Device;
		_args.PointerId = e.PointerId;
		_args.Wheel = Vector2.Zero;

		var preview = PreviewOf(evt);
		if (preview is not null)
		{
			target.RaiseEvent(preview, _args);
			if (_args.Handled) return;
		}

		target.RaiseEvent(evt, _args);
	}

	// The target of the wheel gesture this event continues: the same pointer, not moved past the slop since the last wheel.
	private UIElement? LatchedWheelTarget(in PointerInput e) =>
		_wheelLatch is { } latch && latch.PointerId == e.PointerId
			&& Vector2.DistanceSquared(latch.Position, e.Position) <= WheelLatchSlop * WheelLatchSlop
			? latch.Target
			: null;

	// Raise the wheel event (tunnel Preview then bubble), carrying the scroll delta.
	private void RaiseWheel(UIElement target, in PointerInput e)
	{
		_args.Pointer = new PointerPoint(e.Position, e.Buttons, e.Pressure);
		_args.Device = e.Device;
		_args.PointerId = e.PointerId;
		_args.Wheel = e.Wheel;
		target.RaiseEvent(UIElement.PreviewPointerWheelChangedEvent, _args);
		if (_args.Handled)
			return;
		target.RaiseEvent(UIElement.PointerWheelChangedEvent, _args);
	}

	// Raise a Direct event (enter/exit) with no Preview counterpart.
	private void RaiseDirect(RoutedEvent evt, UIElement target, in PointerInput e)
	{
		_args.Pointer = new PointerPoint(e.Position, e.Buttons, e.Pressure);
		_args.Device = e.Device;
		_args.PointerId = e.PointerId;
		_args.Wheel = Vector2.Zero;
		target.RaiseEvent(evt, _args);
	}

	private void RaiseCaptureLost(UIElement target) =>
		target.RaiseEvent(UIElement.PointerCaptureLostEvent, _captureArgs);

	private static RoutedEvent? PreviewOf(RoutedEvent evt)
	{
		if (ReferenceEquals(evt, UIElement.PointerPressedEvent)) return UIElement.PreviewPointerPressedEvent;
		if (ReferenceEquals(evt, UIElement.PointerReleasedEvent)) return UIElement.PreviewPointerReleasedEvent;
		if (ReferenceEquals(evt, UIElement.PointerMovedEvent)) return UIElement.PreviewPointerMovedEvent;
		return null;
	}

	// Surface priority: topmost screen layer wins.
	internal static UIElement? HitTest(Vector2 pos, IReadOnlyList<UIView> views)
	{
		UIElement? best = null;
		var bestZ = int.MinValue;
		//Used in hotpath, so we avoid the enumerator
		for (var index = 0; index < views.Count; index++)
		{
			var view = views[index];
			if (!view.Interactive)
				continue;
			if (view.Root.HitTest(pos) is { } hit && view.Layer.ZLayer >= bestZ)
			{
				best = hit;
				bestZ = view.Layer.ZLayer;
			}
		}

		return best;
	}

	private static bool Contains(List<UIElement> chain, UIElement el)
	{
		foreach (var item in chain)
		{
			if (ReferenceEquals(item, el))
				return true;
		}
		return false;
	}

	private static bool IsPress(PointerUpdateKind k) =>
		k is PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed;

	private static bool IsRelease(PointerUpdateKind k) =>
		k is PointerUpdateKind.LeftButtonReleased or PointerUpdateKind.RightButtonReleased or PointerUpdateKind.MiddleButtonReleased;

	internal void DumpState(StringBuilder sb)
	{
		foreach (var (id, chain) in _overChains.OrderBy(kvp => kvp.Key))
		{
			sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"pointer {id}: over=[");
			for (var i = 0; i < chain.Count; i++)
			{
				if (i > 0)
					sb.Append(", ");
				sb.Append(chain[i].GetType().Name);
			}
			var cap = GetCapture(id);
			sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"] capture={cap?.GetType().Name ?? "none"}\n");
		}
	}
}
