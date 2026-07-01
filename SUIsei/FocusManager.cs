using System.Text;
using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei;

/// <summary>
/// Per-host focus state machine. Tracks the focused element, raises the four focus
/// routed events (GettingFocus/LosingFocus tunnel, GotFocus/LostFocus bubble), and drives Tab/Shift-Tab
/// navigation across a set of <see cref="UIView"/>s respecting <see cref="UIElement.IsTabStop"/>,
/// <see cref="UIElement.IsEnabled"/>, <see cref="UIElement.TabIndex"/>, and focus scopes.
/// </summary>
public sealed class FocusManager
{
	private readonly GettingFocusEventArgs _gettingArgs = new();
	private readonly RoutedEventArgs _gotArgs = new();

	/// <summary>The element that currently holds keyboard focus, or null.</summary>
	public UIElement? FocusedElement { get; private set; }

	/// <summary>How focus was last acquired.</summary>
	public FocusState FocusState { get; private set; }

	/// <summary>
	/// Move focus to <paramref name="newEl"/> with the given <paramref name="state"/>. Raises
	/// GettingFocus (tunnel on <paramref name="newEl"/>) and LosingFocus (tunnel on the old focused
	/// element); if neither cancels, transfers focus and raises GotFocus + LostFocus bubbling events.
	/// Passing null clears focus. Returns false if the transfer was cancelled or the target is not focusable.
	/// </summary>
	public bool TrySetFocus(UIElement? newEl, FocusState state)
	{
		if (newEl is not null && (!newEl.IsTabStop || !newEl.IsEnabled || newEl.Visibility != Visibility.Visible))
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
		FocusState     = newEl is null ? FocusState.Unfocused : state;

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
			if (el.IsTabStop && el.IsEnabled && el.Visibility == Visibility.Visible)
				return TrySetFocus(el, FocusState.Pointer);
		return false;
	}

	/// <summary>
	/// Move focus to the next or previous tab stop within the active focus scope.
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

		// Arrow directions fall back to Next/Previous; there is no spatial navigation.
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
		if (el.Visibility == Visibility.Collapsed || !el.IsEnabled)
			return;
		if (el.IsTabStop)
			result.Add(el);
		foreach (var child in el.VisualChildren)
			CollectTabStops(child, result);
	}

	internal void DumpState(StringBuilder sb) =>
		sb.Append($"focus={FocusedElement?.GetType().Name ?? "none"}\n");
}
