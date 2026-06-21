using System.Numerics;
using System.Text;

namespace SUIsei;

/// <summary>
/// Per-host pointer routing (PlanningUI.md §D5.1/§D5.4): hit-tests each queued <see cref="PointerInput"/> over the
/// host's interactive overlay views, raises tunnel-then-bubble pointer routed events, tracks the "over" chain (so
/// enter/exit fire per element), implicitly captures on press so a press→release pair routes to the same target
/// (the basis for <c>Button.Click</c>), and supports explicit drag capture via
/// <see cref="RequestCapture"/>/<see cref="RequestRelease"/>. Persistent host state; headless (no engine/backend, §D8).
/// </summary>
public sealed class InputManager
{
	private readonly Dictionary<uint, List<UIElement>> _overChains = [];
	private readonly Dictionary<uint, UIElement?> _implicitCapture = [];
	private readonly Dictionary<uint, UIElement>  _explicitCapture = [];
	private readonly PointerRoutedEventArgs _args = new();
	private readonly RoutedEventArgs _captureArgs = new();
	private List<UIElement> _scratch = [];

	/// <summary>Invoked by <see cref="UIHost"/> on every pointer press so it can update keyboard focus.</summary>
	internal Action<UIElement?>? OnPress { get; set; }

	/// <summary>Route one pointer event over <paramref name="views"/> (called by <see cref="UIHost.Tick"/>).</summary>
	internal void Process(in PointerInput e, IReadOnlyList<UIView> views)
	{
		var hit = HitTest(e.Position, views);

		// A wheel event is its own routed event (no over-chain / move semantics): route it to the captee or the
		// element under the pointer and stop.
		if (e.Wheel != Vector2.Zero)
		{
			var wheelTarget = GetCapture(e.PointerId) ?? hit;
			if (wheelTarget is not null)
				RaiseWheel(wheelTarget, e);
			return;
		}

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
		}
		else
		{
			var target = captee ?? hit;
			if (target is not null)
				Raise(UIElement.PointerMovedEvent, target, e);
		}
	}

	/// <summary>The element currently captured for <paramref name="pointerId"/> — explicit if set, else implicit
	/// press-capture.</summary>
	public UIElement? GetCapture(uint pointerId) =>
		_explicitCapture.GetValueOrDefault(pointerId) ?? _implicitCapture.GetValueOrDefault(pointerId);

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

	// Raise a bubble event preceded by its tunnel Preview counterpart. If the Preview is handled, the bubble is skipped.
	private void Raise(RoutedEvent evt, UIElement target, in PointerInput e)
	{
		_args.Pointer = new PointerPoint(e.Position, e.Buttons, e.Pressure);
		_args.Device = e.Device;

		var preview = PreviewOf(evt);
		if (preview is not null)
		{
			target.RaiseEvent(preview, _args);
			if (_args.Handled) return;
		}

		target.RaiseEvent(evt, _args);
	}

	// Raise the wheel event (tunnel Preview then bubble), carrying the scroll delta.
	private void RaiseWheel(UIElement target, in PointerInput e)
	{
		_args.Pointer = new PointerPoint(e.Position, e.Buttons, e.Pressure);
		_args.Device = e.Device;
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
		target.RaiseEvent(evt, _args);
	}

	private void RaiseCaptureLost(UIElement target) =>
		target.RaiseEvent(UIElement.PointerCaptureLostEvent, _captureArgs);

	private static RoutedEvent? PreviewOf(RoutedEvent evt)
	{
		if (ReferenceEquals(evt, UIElement.PointerPressedEvent))  return UIElement.PreviewPointerPressedEvent;
		if (ReferenceEquals(evt, UIElement.PointerReleasedEvent)) return UIElement.PreviewPointerReleasedEvent;
		if (ReferenceEquals(evt, UIElement.PointerMovedEvent))    return UIElement.PreviewPointerMovedEvent;
		return null;
	}

	// Surface priority (PlanningUI.md §D5.4): topmost screen layer wins.
	private static UIElement? HitTest(Vector2 pos, IReadOnlyList<UIView> views)
	{
		UIElement? best = null;
		var bestZ = int.MinValue;
		foreach (var view in views)
		{
			if (!view.Interactive || view.Placement.Kind != UIPlacementKind.Overlay)
				continue;
			var content = view.Root.Content;
			if (content is null)
				continue;
			var scale = view.Root.LayoutScale > 0f ? view.Root.LayoutScale : 1f;
			var dip = pos / scale;
			var local = dip - new Vector2(content.LayoutRect.X, content.LayoutRect.Y);
			var hit = content.HitTest(local);
			if (hit is not null && view.Placement.ZLayer >= bestZ)
			{
				best = hit;
				bestZ = view.Placement.ZLayer;
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
