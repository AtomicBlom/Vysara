namespace SUIsei.RoutedEvents;

/// <summary>
/// Event args for <see cref="UIElement.GettingFocusEvent"/> and <see cref="UIElement.LosingFocusEvent"/> (both
/// tunnel). Handlers may set <see cref="Cancel"/> to prevent the focus transfer from completing.
/// </summary>
public sealed class GettingFocusEventArgs : RoutedEventArgs
{
	/// <summary>The element about to receive focus (null when focus is being cleared).</summary>
	public UIElement? NewFocusedElement { get; internal set; }

	/// <summary>The element currently holding focus (null when nothing was focused).</summary>
	public UIElement? OldFocusedElement { get; internal set; }

	/// <summary>The focus acquisition mode for the incoming focus.</summary>
	public FocusState FocusState { get; internal set; }

	/// <summary>Set to true in a handler to cancel the focus transfer.</summary>
	public bool Cancel { get; set; }

	internal void Reset(RoutedEvent evt, object originalSource,
		UIElement? oldEl, UIElement? newEl, FocusState state)
	{
		base.Reset(evt, originalSource);
		OldFocusedElement = oldEl;
		NewFocusedElement = newEl;
		FocusState        = state;
		Cancel            = false;
	}
}
