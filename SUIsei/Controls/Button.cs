using SUIsei.Input;

namespace SUIsei.Controls;

/// <summary>
/// A clickable <see cref="ContentControl"/> (PlanningUI.md §D2.1/§D5.3/§D5.7). <see cref="Click"/> is a bubbling
/// routed event synthesized from a pointer press + release over the button, or from Enter/Space when focused. A
/// <c>CommonStates</c> <see cref="VisualStateManager"/> group animates the background on hover/press; the pointer and
/// key handlers drive <see cref="VisualStateManager.GoToState"/>.
/// </summary>
public sealed class Button : ContentControl
{
	/// <summary>The bubbling routed event raised when the button is clicked.</summary>
	public static readonly RoutedEvent ClickEvent = RoutedEvent.Register(nameof(Click), RoutingStrategy.Bubble);

	private static readonly HdrColor NormalBackground = HdrColor.FromRgb(60, 60, 70);
	private static readonly HdrColor OverBackground = HdrColor.FromRgb(80, 80, 95);
	private static readonly HdrColor PressedBackground = HdrColor.FromRgb(40, 40, 55);

	private bool _isPressed;
	private bool _isPointerOver;

	public Button()
	{
		Background = NormalBackground;

		var common = new VisualStateGroup { Name = "CommonStates" };
		common.States.Add(new VisualState { Name = "Normal", Storyboard = BackgroundTo(OverBackground, NormalBackground) });
		common.States.Add(new VisualState { Name = "PointerOver", Storyboard = BackgroundTo(NormalBackground, OverBackground) });
		common.States.Add(new VisualState { Name = "Pressed", Storyboard = BackgroundTo(OverBackground, PressedBackground) });
		VisualStateManager.SetVisualStateGroups(this, [common]);

		AddHandler(PointerEnteredEvent,  OnPointerEntered);
		AddHandler(PointerExitedEvent,   OnPointerExited);
		AddHandler(PointerPressedEvent,  OnPointerPressed);
		AddHandler(PointerReleasedEvent, OnPointerReleased);
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

	// A short background-colour transition between two states, applied on the no-relayout visual path.
	private Storyboard BackgroundTo(HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.HoldEnd,
				Apply = SetAnimatedFill, Release = ClearAnimatedFill,
			},
		},
	};

	private void GoToState(string state) => VisualStateManager.GoToState(this, state, useTransitions: true);

	private void OnPointerEntered(object sender, RoutedEventArgs e) { _isPointerOver = true; GoToState("PointerOver"); }

	private void OnPointerExited(object sender, RoutedEventArgs e)
	{
		_isPointerOver = false;
		_isPressed = false;
		GoToState("Normal");
	}

	private void OnPointerPressed(object sender, RoutedEventArgs e) { _isPressed = true; GoToState("Pressed"); }

	private void OnPointerReleased(object sender, RoutedEventArgs e)
	{
		if (_isPressed)
		{
			_isPressed = false;
			PerformClick();
		}
		GoToState(_isPointerOver ? "PointerOver" : "Normal");
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key) return;
		if (key.Key is VirtualKey.Enter or VirtualKey.Space)
			PerformClick();
	}
}
