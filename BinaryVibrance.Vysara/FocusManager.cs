using System.Text;
using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara;

/// <summary>
/// Per-host focus state machine. Tracks the focused element, raises the four focus
/// routed events (GettingFocus/LosingFocus tunnel, GotFocus/LostFocus bubble), and drives Tab/Shift-Tab
/// navigation across a set of <see cref="UIView"/>s respecting <see cref="UIElement.IsTabStop"/>,
/// <see cref="Controls.Control.IsEnabled"/> and <see cref="UIElement.TabIndex"/>. Tab order runs across all the views as one
/// sequence: no subtree keeps focus within itself, and <see cref="Controls.Control.TabFocusNavigation"/> is not read.
/// Up, down, left and right (a gamepad's d-pad, <see cref="UIHost"/>) move focus by position instead, to the nearest tab
/// stop that way within the focused element's view, or its <see cref="UIElement.XYFocusUp"/> and the like; a control
/// shows how it holds focus in its template's <c>FocusStates</c>.
/// </summary>
public sealed class FocusManager
{
	private readonly GettingFocusEventArgs _gettingArgs = new();
	private readonly RoutedEventArgs _gotArgs = new();
	// Elements focus was taken from as they left the live tree, whose LostFocus RaiseReleased has yet to raise.
	private readonly List<UIElement> _released = [];

	/// <summary>The element that currently holds keyboard focus, or null.</summary>
	public UIElement? FocusedElement { get; private set; }

	/// <summary>
	/// True when an element holds keyboard focus, so this frame's keys route into the UI rather than past it. Mirrors
	/// exactly how the host drains keyboard input — keys are delivered to <see cref="FocusedElement"/> whenever one is
	/// set, regardless of its later visible/tab-stop state (a control disabled since is released before the keys are
	/// drained) — so the report never disagrees with where the keys actually go. The keyboard half of the coarse
	/// capture report.
	/// </summary>
	internal bool WantsKeyboard => FocusedElement is not null;

	/// <summary>How focus was last acquired.</summary>
	public FocusState FocusState { get; private set; }

	// A tab stop that is visible and not in a disabled control (a disabled control, and the parts of its template, take
	// no focus).
	internal static bool IsFocusable(UIElement el) =>
		el.IsTabStop && el.Visibility == Visibility.Visible && !el.InDisabledControl;

	/// <summary>Clear focus when the focused element has since been disabled (it, or the control it is part of), since a
	/// disabled control cannot hold focus. The host calls this each frame before it routes keyboard input.</summary>
	internal void ReleaseDisabledFocus()
	{
		if (FocusedElement is { InDisabledControl: true })
			TrySetFocus(null, FocusState.Programmatic);
	}

	/// <summary>
	/// <paramref name="subtree"/> left the live tree. If the focused element is in it, focus is cleared at once, so keys no
	/// longer go to a detached element, and its LostFocus is queued for <see cref="RaiseReleased"/>, which the host calls
	/// once layout is done: an element can leave in the middle of a layout pass (a virtualizing panel recycling a row), and
	/// no app code runs there. An element that is leaving can't cancel this, so no LosingFocus is raised.
	/// </summary>
	internal void ReleaseWithin(UIElement subtree)
	{
		if (FocusedElement is not { } focused || !focused.IsWithin(subtree))
			return;
		FocusedElement = null;
		FocusState = FocusState.Unfocused;
		_released.Add(focused);
	}

	/// <summary>
	/// Raise LostFocus on each element focus was taken from as it left the live tree (<see cref="ReleaseWithin"/>), so it
	/// can stop what focus started (a caret's blink); one focused again since is left alone. Called by the host outside
	/// layout. A handler that makes another element leave queues it here, and it is raised in the same call.
	/// </summary>
	internal void RaiseReleased()
	{
		for (var i = 0; i < _released.Count; i++)
		{
			var element = _released[i];
			if (ReferenceEquals(element, FocusedElement))
				continue;
			_gotArgs.Reset(UIElement.LostFocusEvent, element);
			element.RaiseEvent(UIElement.LostFocusEvent, _gotArgs);
		}
		_released.Clear();
	}

	/// <summary>
	/// Move focus to <paramref name="newEl"/> with the given <paramref name="state"/>. Raises
	/// GettingFocus (tunnel on <paramref name="newEl"/>) and LosingFocus (tunnel on the old focused
	/// element); if neither cancels, transfers focus and raises GotFocus + LostFocus bubbling events.
	/// Passing null clears focus. Returns false if the transfer was cancelled or the target is not focusable.
	/// </summary>
	public bool TrySetFocus(UIElement? newEl, FocusState state)
	{
		if (newEl is not null && !IsFocusable(newEl))
			return false;

		var oldEl = FocusedElement;
		if (ReferenceEquals(oldEl, newEl))
			return true;

		// GettingFocus tunnel on the new element (if any)
		if (newEl is not null)
		{
			_gettingArgs.Reset(UIElement.GettingFocusEvent, newEl, oldEl, newEl, state);
			newEl.RaiseEvent(UIElement.GettingFocusEvent, _gettingArgs);
			if (_gettingArgs.Cancel) return false;
		}

		// LosingFocus tunnel on the old element (if any)
		if (oldEl is not null)
		{
			_gettingArgs.Reset(UIElement.LosingFocusEvent, oldEl, oldEl, newEl, state);
			oldEl.RaiseEvent(UIElement.LosingFocusEvent, _gettingArgs);
			if (_gettingArgs.Cancel) return false;
		}

		FocusedElement = newEl;
		FocusState = newEl is null ? FocusState.Unfocused : state;
		(oldEl as Controls.Control)?.ShowFocus(FocusState.Unfocused);
		(newEl as Controls.Control)?.ShowFocus(state);

		// GotFocus bubble on new element
		if (newEl is not null)
		{
			_gotArgs.Reset(UIElement.GotFocusEvent, newEl);
			newEl.RaiseEvent(UIElement.GotFocusEvent, _gotArgs);
		}

		// LostFocus bubble on old element
		if (oldEl is not null)
		{
			_gotArgs.Reset(UIElement.LostFocusEvent, oldEl);
			oldEl.RaiseEvent(UIElement.LostFocusEvent, _gotArgs);
		}

		return true;
	}

	/// <summary>
	/// Focus the nearest focusable element at or above <paramref name="hit"/> in the tree —
	/// the pointer-press path. A click that lands on a non-focusable part (e.g. a control's template root or its
	/// content) focuses the control itself, matching WinUI. Returns false when no focusable ancestor exists (focus is
	/// left unchanged).
	/// </summary>
	public bool FocusFromPointer(UIElement? hit)
	{
		for (var el = hit; el is not null; el = el.Parent)
			if (IsFocusable(el))
				return TrySetFocus(el, FocusState.Pointer);
		return false;
	}

	/// <summary>
	/// Move focus to the next or previous tab stop across <paramref name="views"/>, wrapping at either end.
	/// Returns false when no focusable elements exist in <paramref name="views"/>.
	/// </summary>
	public bool MoveFocus(FocusNavigationDirection dir, IReadOnlyList<UIView> views)
	{
		var candidates = CollectTabStops(views);
		if (candidates.Count == 0)
			return false;

		if (dir is FocusNavigationDirection.Next or FocusNavigationDirection.Previous)
		{
			var idx = FocusedElement is not null ? candidates.IndexOf(FocusedElement) : -1;
			int next;
			if (dir == FocusNavigationDirection.Next)
				next = idx < 0 ? 0 : (idx + 1) % candidates.Count;
			else
				next = idx <= 0 ? candidates.Count - 1 : idx - 1;
			return TrySetFocus(candidates[next], FocusState.Keyboard);
		}

		return MoveFocusSpatially(dir, views);
	}

	/// <summary>
	/// Up, down, left or right: focus the nearest tab stop that way from the focused element (its
	/// <see cref="UIElement.XYFocusUp"/> and the like first, where set), among the tab stops of the view it is in, as a
	/// gamepad's d-pad moves through a page. With nothing focused, the first tab stop takes focus. Nearest is by the gap
	/// between the facing edges, with any offset across the direction counting double, so a control straight ahead beats
	/// a nearer one off to the side. Returns false when there is nothing that way (focus stays; it doesn't wrap).
	/// </summary>
	private bool MoveFocusSpatially(FocusNavigationDirection dir, IReadOnlyList<UIView> views)
	{
		if (FocusedElement is not { } from || !IsFocusable(from))
		{
			var first = CollectTabStops(views);
			return first.Count > 0 && TrySetFocus(first[0], FocusState.Keyboard);
		}

		var explicitTarget = dir switch
		{
			FocusNavigationDirection.Up => from.XYFocusUp,
			FocusNavigationDirection.Down => from.XYFocusDown,
			FocusNavigationDirection.Left => from.XYFocusLeft,
			_ => from.XYFocusRight,
		};
		if (explicitTarget is not null && IsFocusable(explicitTarget))
			return TrySetFocus(explicitTarget, FocusState.Keyboard);

		// Only the view focus is in: a menu over the page keeps the d-pad to itself
		var scope = views.Where(v => IsIn(from, v)).ToArray();
		var candidates = CollectTabStops(scope.Length > 0 ? scope : views);
		var a = BoundsOf(from);
		UIElement? best = null;
		float bestScore = float.MaxValue;
		foreach (var c in candidates)
		{
			if (ReferenceEquals(c, from))
				continue;
			if (Score(a, BoundsOf(c), dir) is { } score && score < bestScore)
			{
				best = c;
				bestScore = score;
			}
		}
		return best is not null && TrySetFocus(best, FocusState.Keyboard);
	}

	// How far b lies from a in a direction, or null when it doesn't lie that way: the gap between their facing edges
	// (0 if they overlap along it), plus twice the gap across it (0 if they overlap across it), plus a hundredth of the
	// distance between their centres to break ties
	internal static float? Score(Rect a, Rect b, FocusNavigationDirection dir)
	{
		float acx = a.X + a.Width / 2f, acy = a.Y + a.Height / 2f, bcx = b.X + b.Width / 2f, bcy = b.Y + b.Height / 2f;
		const float Slack = 0.5f;
		float ahead, across;
		switch (dir)
		{
			case FocusNavigationDirection.Down:
				if (bcy <= acy + Slack || b.Bottom <= a.Bottom) return null;
				ahead = Math.Max(0f, b.Y - a.Bottom);
				across = Math.Max(0f, Math.Max(b.X - a.Right, a.X - b.Right));
				break;
			case FocusNavigationDirection.Up:
				if (bcy >= acy - Slack || b.Y >= a.Y) return null;
				ahead = Math.Max(0f, a.Y - b.Bottom);
				across = Math.Max(0f, Math.Max(b.X - a.Right, a.X - b.Right));
				break;
			case FocusNavigationDirection.Right:
				if (bcx <= acx + Slack || b.Right <= a.Right) return null;
				ahead = Math.Max(0f, b.X - a.Right);
				across = Math.Max(0f, Math.Max(b.Y - a.Bottom, a.Y - b.Bottom));
				break;
			default:
				if (bcx >= acx - Slack || b.X >= a.X) return null;
				ahead = Math.Max(0f, a.X - b.Right);
				across = Math.Max(0f, Math.Max(b.Y - a.Bottom, a.Y - b.Bottom));
				break;
		}
		float centres = MathF.Sqrt((bcx - acx) * (bcx - acx) + (bcy - acy) * (bcy - acy));
		return ahead + 2f * across + centres * 0.01f;
	}

	// An element's box on its surface, as last laid out
	private static Rect BoundsOf(UIElement el)
		=> el.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, el.LayoutRect.Width, el.LayoutRect.Height));

	// Whether el is in view's tree: it, or an ancestor, is the view's root or its content
	private static bool IsIn(UIElement el, UIView view)
	{
		for (UIElement? at = el; at is not null; at = at.Parent)
			if (ReferenceEquals(at, view.Root) || ReferenceEquals(at, view.Root.Content))
				return true;
		return false;
	}

	// DFS collection of tab-focusable elements sorted by (TabIndex, document order).
	private static List<UIElement> CollectTabStops(IReadOnlyList<UIView> views)
	{
		var all = new List<UIElement>();
		foreach (var view in views)
		{
			if (!view.Interactive || view.Root.Content is null)
				continue;
			CollectTabStops(view.Root.Content, all);
		}
		all.Sort(static (a, b) =>
		{
			var cmp = a.TabIndex.CompareTo(b.TabIndex);
			return cmp != 0 ? cmp : 0; // equal-index elements keep DFS (document) order
		});
		return all;
	}

	private static void CollectTabStops(UIElement el, List<UIElement> result)
	{
		if (el.Visibility == Visibility.Collapsed || el is Controls.Control { IsEnabled: false })
			return;
		if (el.IsTabStop)
			result.Add(el);
		foreach (var child in el.VisualChildren)
			CollectTabStops(child, result);
	}

	internal void DumpState(StringBuilder sb) =>
		sb.Append($"focus={FocusedElement?.GetType().Name ?? "none"}\n");
}
