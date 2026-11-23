using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Controls.Primitives;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// A switch that is on or off, as WinUI's <c>ToggleSwitch</c>: a pill-shaped track whose knob sits left when off and
/// right, on the accent, when on, beside <see cref="OnContent"/> or <see cref="OffContent"/>, under an optional
/// <see cref="Header"/>. A click, Enter, Space or the pad's A flips <see cref="IsOn"/>, raising <see cref="Toggled"/>;
/// <see cref="IsOnChanged"/> lets it bind TwoWay. The track and knob take the <c>ToggleSwitchFillOn</c>,
/// <c>ToggleSwitchStrokeOff</c>, <c>ToggleSwitchKnobFillOn</c> and <c>ToggleSwitchKnobFillOff</c> resources. Disabled,
/// the track and knob take the shared disabled brushes, on or off, and the text dims.
/// </summary>
public sealed class ToggleSwitch : Control
{
	private static readonly SolidColorBrush FillOn = Brush.Sealed(HdrColor.FromRgb(80, 150, 240));
	private static readonly SolidColorBrush FillOff = Brush.Sealed(HdrColor.Transparent);
	private static readonly SolidColorBrush StrokeOff = Brush.Sealed(HdrColor.FromRgb(160, 160, 170));
	private static readonly SolidColorBrush KnobOn = Brush.Sealed(HdrColor.FromRgb(16, 20, 28));
	private static readonly SolidColorBrush KnobOff = Brush.Sealed(HdrColor.FromRgb(200, 200, 210));
	private static readonly SolidColorBrush OverFill = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 20));

	public static readonly DependencyProperty<ToggleSwitch, bool> IsOnProperty =
		DependencyProperty.Register(nameof(IsOn),
			new PropertyMetadata<ToggleSwitch, bool> { PropertyChanged = static (t, _, _) => t.OnIsOnChanged() });

	public static readonly DependencyProperty<ToggleSwitch, object?> HeaderProperty =
		DependencyProperty.Register(nameof(Header),
			new PropertyMetadata<ToggleSwitch, object?> { PropertyChanged = static (t, _, _) => t.Refresh() });

	public static readonly DependencyProperty<ToggleSwitch, object?> OnContentProperty =
		DependencyProperty.Register(nameof(OnContent),
			new PropertyMetadata<ToggleSwitch, object?> { DefaultValue = "On", PropertyChanged = static (t, _, _) => t.Refresh() });

	public static readonly DependencyProperty<ToggleSwitch, object?> OffContentProperty =
		DependencyProperty.Register(nameof(OffContent),
			new PropertyMetadata<ToggleSwitch, object?> { DefaultValue = "Off", PropertyChanged = static (t, _, _) => t.Refresh() });

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
	public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

	/// <summary>Shown beside the switch while it is on. "On" by default.</summary>
	public object? OnContent { get => GetValue(OnContentProperty); set => SetValue(OnContentProperty, value); }

	/// <summary>Shown beside the switch while it is off. "Off" by default.</summary>
	public object? OffContent { get => GetValue(OffContentProperty); set => SetValue(OffContentProperty, value); }

	/// <summary>Raised as <see cref="IsOn"/> changes, by input or by code, as WinUI's.</summary>
	public event RoutedEventHandler? Toggled;

	/// <summary>Raised as <see cref="IsOn"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? IsOnChanged;

	private protected override string CommonVisualState => _press.State(IsEnabled);

	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		_press.Reset();
		Refresh();
	}

	private void OnIsOnChanged()
	{
		Refresh();
		Toggled?.Invoke(this, new RoutedEventArgs { OriginalSource = this, Source = this });
		IsOnChanged?.Invoke(this, EventArgs.Empty);
	}

	// The parts shown as the switch is now: on or off, enabled or not
	private void Refresh()
	{
		if (_track is null || _knob is null || _state is null || _header is null)
			return;
		var enabled = IsEnabled;
		var on = enabled ? _brushes.On : TemplateStates.DisabledAccent;
		_track.Background = IsOn ? on : FillOff;
		_track.BorderBrush = IsOn ? on : enabled ? _brushes.Off : TemplateStates.DisabledBorder;
		_knob.Background = enabled
			? IsOn ? _brushes.KnobOn : _brushes.KnobOff
			: IsOn ? TemplateStates.DisabledBackground : TemplateStates.DisabledAccent;
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
					(header, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground)),
			});
			VisualStateManager.SetVisualStateGroups(column, [common]);
			self.Refresh();
			return column;
		},
	};
}
