using SUIsei.Input;

namespace SUIsei.Controls;

/// <summary>
/// A clickable <see cref="ContentControl"/> with a default background (PlanningUI.md §D2.1/§D5.3). <see cref="Click"/>
/// is a bubbling routed event synthesized from a pointer press + release over the button, or from Enter/Space when
/// the button has keyboard focus.
/// </summary>
public sealed class Button : ContentControl
{
	/// <summary>The bubbling routed event raised when the button is clicked.</summary>
	public static readonly RoutedEvent ClickEvent = RoutedEvent.Register(nameof(Click), RoutingStrategy.Bubble);

	private bool _isPressed;

	public Button()
	{
		Background = HdrColor.FromRgb(60, 60, 70);
		AddHandler(PointerPressedEvent,  OnPointerPressed);
		AddHandler(PointerReleasedEvent, OnPointerReleased);
		AddHandler(PointerExitedEvent,   OnPointerExited);
		AddHandler(KeyDownEvent,         OnKeyDown);
	}

	/// <summary>Raised when the button is clicked. Wired by the generator (<c>btn.Click += OnClick</c>) — bubbles.</summary>
	public event RoutedEventHandler Click
	{
		add    => AddHandler(ClickEvent, value);
		remove => RemoveHandler(ClickEvent, value);
	}

	/// <summary>Raise <see cref="Click"/> directly (the input pipeline synthesizes it on press→release; public for
	/// headless tests so a click can be driven without the pointer stack).</summary>
	public void PerformClick() => RaiseEvent(ClickEvent, new RoutedEventArgs());

	private void OnPointerPressed(object sender, RoutedEventArgs e) => _isPressed = true;

	private void OnPointerExited(object sender, RoutedEventArgs e) => _isPressed = false;

	private void OnPointerReleased(object sender, RoutedEventArgs e)
	{
		if (!_isPressed)
			return;
		_isPressed = false;
		PerformClick();
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key) return;
		if (key.Key is VirtualKey.Enter or VirtualKey.Space)
			PerformClick();
	}
}
