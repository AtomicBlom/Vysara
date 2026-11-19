using SUIsei.Animation;
using SUIsei.Controls.Primitives;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A switch that is on or off, as WinUI's <c>ToggleSwitch</c>: a pill-shaped track whose knob sits left when off and
/// right, on the accent, when on, beside <see cref="OnContent"/> or <see cref="OffContent"/>, under an optional
/// <see cref="Header"/>. A click, Enter, Space or the pad's A flips <see cref="IsOn"/>, raising <see cref="Toggled"/>;
/// <see cref="IsOnChanged"/> lets it bind TwoWay. The track and knob take the <c>ToggleSwitchFillOn</c>,
/// <c>ToggleSwitchStrokeOff</c>, <c>ToggleSwitchKnobFillOn</c> and <c>ToggleSwitchKnobFillOff</c> resources.
/// </summary>
public sealed class ToggleSwitch : Control
{
	private static readonly SolidColorBrush FillOn = Brush.Sealed(HdrColor.FromRgb(80, 150, 240));
	private static readonly SolidColorBrush FillOff = Brush.Sealed(HdrColor.Transparent);
	private static readonly SolidColorBrush StrokeOff = Brush.Sealed(HdrColor.FromRgb(160, 160, 170));
	private static readonly SolidColorBrush KnobOn = Brush.Sealed(HdrColor.FromRgb(16, 20, 28));
	private static readonly SolidColorBrush KnobOff = Brush.Sealed(HdrColor.FromRgb(200, 200, 210));
	private static readonly SolidColorBrush OverFill = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 20));

	public static readonly RoutedEvent ToggledEvent = RoutedEvent.Register(nameof(Toggled), RoutingStrategy.Bubble);

	public static readonly DependencyProperty<ToggleSwitch, bool> IsOnProperty =
		DependencyProperty.Register(nameof(IsOn),
			new PropertyMetadata<ToggleSwitch, bool> { PropertyChanged = static (t, _, _) => t.OnIsOnChanged() });

	private readonly Pressable _press;
	private Border? _track;
	private Border? _knob;
	private ContentPresenter? _state;
	private ContentPresenter? _header;
	private (Brush On, Brush Off, Brush KnobOn, Brush KnobOff) _brushes = (FillOn, StrokeOff, KnobOn, KnobOff);

	public ToggleSwitch()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		InheritedText.GiveBuiltInLook(this);
		_press = new Pressable(this, () => IsOn = !IsOn, () => GoToCommonState(useTransitions: true));
	}

	/// <summary>Whether the switch is on.</summary>
	public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

	/// <summary>Shown above the switch, or nothing where null.</summary>
	public object? Header { get; set { field = value; Refresh(); } }

	/// <summary>Shown beside the switch while it is on.</summary>
	public object? OnContent { get; set { field = value; Refresh(); } } = "On";

	/// <summary>Shown beside the switch while it is off.</summary>
	public object? OffContent { get; set { field = value; Refresh(); } } = "Off";

	/// <summary>Raised as <see cref="IsOn"/> changes, by input or by code.</summary>
	public event RoutedEventHandler Toggled
	{
		add => AddHandler(ToggledEvent, value);
		remove => RemoveHandler(ToggledEvent, value);
	}

	/// <summary>Raised as <see cref="IsOn"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? IsOnChanged;

	private protected override string CommonVisualState => _press.State(IsEnabled);

	private protected override void OnIsEnabledChanged(bool isEnabled) => _press.Reset();

	private void OnIsOnChanged()
	{
		Refresh();
		RaiseEvent(ToggledEvent, new RoutedEventArgs());
		IsOnChanged?.Invoke(this, EventArgs.Empty);
	}

	// The parts shown as the switch is now
	private void Refresh()
	{
		if (_track is null || _knob is null || _state is null || _header is null)
			return;
		_track.Background = IsOn ? _brushes.On : FillOff;
		_track.BorderBrush = IsOn ? _brushes.On : _brushes.Off;
		_knob.Background = IsOn ? _brushes.KnobOn : _brushes.KnobOff;
		_knob.HorizontalAlignment = IsOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
		_state.Content = IsOn ? OnContent : OffContent;
		_header.Content = Header;
		_header.Visibility = Header is null ? Visibility.Collapsed : Visibility.Visible;
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (ToggleSwitch)control;
			self._brushes = (self.ThemeResource("ToggleSwitchFillOn", FillOn), self.ThemeResource("ToggleSwitchStrokeOff", StrokeOff),
				self.ThemeResource("ToggleSwitchKnobFillOn", KnobOn), self.ThemeResource("ToggleSwitchKnobFillOff", KnobOff));
			var column = new StackPanel { Orientation = Orientation.Vertical };
			var header = new ContentPresenter { Margin = new Thickness(0f, 0f, 0f, 6f) };
			var row = new StackPanel { Orientation = Orientation.Horizontal };
			var track = new Border
			{
				Width = 40f,
				Height = 20f,
				CornerRadius = new CornerRadius(10f),
				BorderThickness = new Thickness(1.5f),
				Padding = new Thickness(3f, 0f, 3f, 0f),
				VerticalAlignment = VerticalAlignment.Center,
				BackgroundTransition = TemplateStates.Fade(120),
			};
			var knob = new Border { Width = 12f, Height = 12f, CornerRadius = new CornerRadius(6f), VerticalAlignment = VerticalAlignment.Center };
			track.Child = knob;
			var state = new ContentPresenter { Margin = new Thickness(10f, 0f, 0f, 0f), VerticalAlignment = VerticalAlignment.Center };
			self.RegisterTemplateBinding(ForegroundProperty, state, ContentPresenter.ForegroundProperty);
			self.RegisterTemplateBinding(ForegroundProperty, header, ContentPresenter.ForegroundProperty);
			row.Children.Add(track);
			row.Children.Add(state);
			column.Children.Add(header);
			column.Children.Add(row);
			(self._track, self._knob, self._state, self._header) = (track, knob, state, header);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Hold((row, Panel.BackgroundProperty, OverFill)) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Hold((row, Panel.BackgroundProperty, OverFill)) });
			common.States.Add(new VisualState
			{
				Name = "Disabled",
				Storyboard = TemplateStates.Hold((state, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground),
					(knob, Border.BackgroundProperty, TemplateStates.DisabledAccent)),
			});
			VisualStateManager.SetVisualStateGroups(column, [common]);
			self.Refresh();
			return column;
		},
	};
}
