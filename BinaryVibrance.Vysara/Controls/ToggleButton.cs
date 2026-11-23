using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// A <see cref="ButtonBase"/> that holds a checked state — the base for <see cref="CheckBox"/>.
/// A click cycles <see cref="IsChecked"/> (two-state, or three-state through <c>null</c> when
/// <see cref="IsThreeState"/> is set), raising <see cref="Checked"/>/<see cref="Unchecked"/>/<see cref="Indeterminate"/>
/// and driving the <c>CheckStates</c> visual-state group. Subclasses reflect the state in template parts via
/// <see cref="OnToggleStateChanged"/>.
/// </summary>
public class ToggleButton : ButtonBase
{
	// The built-in look's brushes, shared by every default toggle and never changed.
	private static readonly SolidColorBrush NormalBackground = Brush.Sealed(HdrColor.FromRgb(60, 60, 70));
	private static readonly SolidColorBrush OverBackground = Brush.Sealed(HdrColor.FromRgb(80, 80, 95));
	private static readonly SolidColorBrush PressedBackground = Brush.Sealed(HdrColor.FromRgb(40, 40, 55));
	private static readonly SolidColorBrush AccentBackground = Brush.Sealed(HdrColor.FromRgb(70, 120, 200));

	public ToggleButton()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		SetBuiltInValue(PaddingProperty, Button.BuiltInPadding);
		InheritedText.GiveBuiltInLook(this);
	}

	public static readonly RoutedEvent CheckedEvent = RoutedEvent.Register(nameof(Checked), RoutingStrategy.Bubble);
	public static readonly RoutedEvent UncheckedEvent = RoutedEvent.Register(nameof(Unchecked), RoutingStrategy.Bubble);
	public static readonly RoutedEvent IndeterminateEvent = RoutedEvent.Register(nameof(Indeterminate), RoutingStrategy.Bubble);

	public static readonly DependencyProperty<ToggleButton, bool?> IsCheckedProperty =
		DependencyProperty.Register(nameof(IsChecked),
			new PropertyMetadata<ToggleButton, bool?> { DefaultValue = false, PropertyChanged = static (t, _, _) => t.OnIsCheckedChanged() });

	public static readonly DependencyProperty<ToggleButton, bool> IsThreeStateProperty =
		DependencyProperty.Register(nameof(IsThreeState),
			new PropertyMetadata<ToggleButton, bool> { DefaultValue = false });

	/// <summary>The checked state: <c>false</c>, <c>true</c>, or <c>null</c> (indeterminate, only reached when
	/// <see cref="IsThreeState"/> is set).</summary>
	public bool? IsChecked
	{
		get => GetValue(IsCheckedProperty);
		set => SetValue(IsCheckedProperty, value);
	}

	/// <summary>When set, a click cycles through the <c>null</c> (indeterminate) state as well.</summary>
	public bool IsThreeState
	{
		get => GetValue(IsThreeStateProperty);
		set => SetValue(IsThreeStateProperty, value);
	}

	public event RoutedEventHandler Checked
	{
		add => AddHandler(CheckedEvent, value);
		remove => RemoveHandler(CheckedEvent, value);
	}

	public event RoutedEventHandler Unchecked
	{
		add => AddHandler(UncheckedEvent, value);
		remove => RemoveHandler(UncheckedEvent, value);
	}

	public event RoutedEventHandler Indeterminate
	{
		add => AddHandler(IndeterminateEvent, value);
		remove => RemoveHandler(IndeterminateEvent, value);
	}

	/// <summary>
	/// Raised whenever <see cref="IsChecked"/> changes, by a click or from code, after <see cref="Checked"/>,
	/// <see cref="Unchecked"/> or <see cref="Indeterminate"/> and the visual state. It is the change event a
	/// <c>{x:Bind IsSelected, Mode=TwoWay}</c> on <c>IsChecked</c> writes back through.
	/// </summary>
	public event EventHandler? IsCheckedChanged;

	protected override void OnClick()
	{
		OnToggle();
		base.OnClick();
	}

	/// <summary>Advance the checked state on click: false → true → (three-state) null → false.</summary>
	protected virtual void OnToggle() => IsChecked = IsChecked switch
	{
		false => true,
		true => IsThreeState ? null : false,
		_ => false,
	};

	/// <summary>Hook for subclasses to reflect the current state in template parts (e.g. a check glyph). Called on
	/// every state change and once the template is applied.</summary>
	protected virtual void OnToggleStateChanged() { }

	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		// Snap a freshly realized tree to the current check state: the accent fades up from hidden, so a transition
		// here would fade a checked toggle's accent in each time its template is applied.
		UpdateVisualState(useTransitions: false);
	}

	private void OnIsCheckedChanged()
	{
		RaiseEvent(IsChecked switch { true => CheckedEvent, false => UncheckedEvent, _ => IndeterminateEvent }, new RoutedEventArgs());
		UpdateVisualState();
		IsCheckedChanged?.Invoke(this, EventArgs.Empty);
	}

	private void UpdateVisualState(bool useTransitions = true)
	{
		// Best-effort for a VSM-driven template; harmless (returns false) for a template that reflects the state
		// through parts instead. The part-based path is the OnToggleStateChanged hook.
		VisualStateManager.GoToState(this, IsChecked switch { true => "Checked", null => "Indeterminate", _ => "Unchecked" }, useTransitions);
		OnToggleStateChanged();

		ShowForeground();
	}

	// The built-in template's presenter, and the text it shows while checked where ToggleButtonForegroundChecked gives one.
	private ContentPresenter? _builtInPresenter;
	private Brush? _checkedForeground;

	// The built-in presenter's text: the checked text while checked where there is one, else the toggle's Foreground.
	private void ShowForeground() =>
		_builtInPresenter?.SetTemplateValue(ContentPresenter.ForegroundProperty,
			IsChecked == true && _checkedForeground is { } checkedForeground ? checkedForeground : Foreground);

	// The default toggle look: a rounded button whose pointer states show their backgrounds on the base border (faded by
	// its BrushTransition), layered under an accent
	// overlay whose opacity the CheckStates group drives — so checked/unchecked cross-fades the accent in and out
	// without fighting the hover/press background (two states, two properties, no conflict). CheckBox replaces this
	// template entirely with its box-and-glyph look. As WinUI's, the accent is ToggleButtonBackgroundChecked and the text
	// on it ToggleButtonForegroundChecked where a page's resources give them.
	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var toggle = (ToggleButton)control;
			var root = new Grid();

			var baseBorder = new Border { CornerRadius = new CornerRadius(4f), BackgroundTransition = TemplateStates.Fade(120) };
			var accent = new Border { Background = AccentBackground, CornerRadius = new CornerRadius(4f), Opacity = 0f };
			var presenter = new ContentPresenter
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			// As WinUI's default template: the base border paints the toggle's Background, BorderBrush and BorderThickness,
			// the presenter its Foreground (or the checked text's, while checked), and Padding is the presenter's margin.
			toggle._builtInPresenter = presenter;
			var background = toggle.ThemeResource("ToggleButtonBackground", NormalBackground);
			var over = toggle.ThemeResource("ToggleButtonBackgroundPointerOver", OverBackground);
			var pressed = toggle.ThemeResource("ToggleButtonBackgroundPressed", PressedBackground);
			TemplateStates.ThemedForeground(toggle, "ToggleButtonForeground");
			toggle.RegisterTemplateBinding(BackgroundProperty, baseBorder, Border.BackgroundProperty, value => value ?? background);
			toggle.RegisterTemplateBinding(BorderBrushProperty, baseBorder, Border.BorderBrushProperty);
			toggle.RegisterTemplateBinding(BorderThicknessProperty, baseBorder, Border.BorderThicknessProperty);
			toggle.RegisterTemplateBinding(PaddingProperty, presenter, FrameworkElement.MarginProperty);
			toggle.RegisterTemplateBinding(ForegroundProperty, _ => toggle.ShowForeground());
			toggle.RegisterContentTemplateBinding(content => presenter.Content = content);
			accent.Background = toggle.ThemeResource("ToggleButtonBackgroundChecked", AccentBackground);
			toggle._checkedForeground = toggle.ThemeResourceOrNull("ToggleButtonForegroundChecked");
			toggle.ShowForeground();

			root.Children.Add(baseBorder);
			root.Children.Add(accent);
			root.Children.Add(presenter);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(baseBorder, over) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(baseBorder, pressed) });
			// Disabled mutes the base and the label as a button's does, and the accent too, so a checked toggle still reads as
			// checked but no longer in the accent colour.
			common.States.Add(new VisualState
			{
				Name = "Disabled",
				Storyboard = TemplateStates.Hold(
					(baseBorder, Border.BackgroundProperty, TemplateStates.DisabledBackground),
					(baseBorder, Border.BorderBrushProperty, TemplateStates.DisabledBorder),
					(accent, Border.BackgroundProperty, TemplateStates.DisabledAccent),
					(presenter, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground)),
			});

			var check = new VisualStateGroup { Name = "CheckStates" };
			check.States.Add(new VisualState { Name = "Unchecked", Storyboard = OpacityTo(accent, 0f) });
			check.States.Add(new VisualState { Name = "Checked", Storyboard = OpacityTo(accent, 1f) });
			check.States.Add(new VisualState { Name = "Indeterminate", Storyboard = OpacityTo(accent, 0.5f) });

			VisualStateManager.SetVisualStateGroups(root, [common, check]);
			return root;
		},
	};

	// The accent overlay's opacity fades to `to` from wherever it shows, its resting local 0 at first. Every check state
	// animates it through Target and TargetProperty, so a change of state hands the opacity from one state's animation to
	// the next, and the accent fades rather than dropping to 0 for a frame as the old state is released.
	private static Storyboard OpacityTo(UIElement target, float to) => new()
	{
		Children =
		{
			new DoubleAnimation
			{
				To = to, Duration = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.HoldEnd,
				Target = target, TargetProperty = UIElement.OpacityProperty,
			},
		},
	};
}
