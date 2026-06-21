using SUIsei.Input;

namespace SUIsei.Controls;

/// <summary>
/// A clickable <see cref="ContentControl"/> (PlanningUI.md §D2.1/§D5.3/§D5.7). Its look comes from a default
/// <see cref="ControlTemplate"/> — a rounded <see cref="Border"/> hosting a <see cref="ContentPresenter"/>
/// (<c>{TemplateBinding Content}</c>) with a <c>CommonStates</c> <see cref="VisualStateManager"/> group that animates
/// the background on hover/press. <see cref="Click"/> is a bubbling routed event synthesized from a pointer press +
/// release over the button, or from Enter/Space when focused; the pointer/key handlers also drive the visual states.
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
		Template = DefaultTemplate;
		AddHandler(PointerEnteredEvent,  OnPointerEntered);
		AddHandler(PointerExitedEvent,   OnPointerExited);
		AddHandler(PointerPressedEvent,  OnPointerPressed);
		AddHandler(PointerReleasedEvent, OnPointerReleased);
		AddHandler(KeyDownEvent,         OnKeyDown);
	}

	// The built-in look, shared (stateless) across default buttons: a rounded Border hosting a ContentPresenter,
	// with a CommonStates group animating the border background. The storyboards target the border created here, so
	// they (and the VSM groups) are built in the factory rather than the constructor.
	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var host = (ContentControl)control;
			var border = new Border
			{
				Background = NormalBackground,
				CornerRadius = 4f,
				Padding = new Thickness(10f, 5f, 10f, 5f),
			};
			var presenter = new ContentPresenter();
			border.Child = presenter;
			presenter.Content = host.Content;                        // {TemplateBinding Content} — initial
			host.ContentChanged += (_, _) => presenter.Content = host.Content; // live

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = BackgroundTo(border, OverBackground, NormalBackground) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = BackgroundTo(border, NormalBackground, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = BackgroundTo(border, OverBackground, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};

	/// <summary>Raised when the button is clicked. Wired by the generator (<c>btn.Click += OnClick</c>) — bubbles.</summary>
	public event RoutedEventHandler Click
	{
		add    => AddHandler(ClickEvent, value);
		remove => RemoveHandler(ClickEvent, value);
	}

	/// <summary>Raise <see cref="Click"/> directly (the input pipeline synthesizes it on press→release; public for
	/// headless tests so a click can be driven without the pointer stack).</summary>
	public void PerformClick() => RaiseEvent(ClickEvent, new RoutedEventArgs());

	// A short background-colour transition between two states, applied to the template's border on the no-relayout
	// visual path.
	private static Storyboard BackgroundTo(Border target, HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.HoldEnd,
				Apply = target.SetAnimatedFill, Release = target.ClearAnimatedFill,
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
