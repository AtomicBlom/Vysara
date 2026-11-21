using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// One of a set of choices, as WinUI's <c>RadioButton</c>: a click checks it, never unchecks it, and checking it unchecks
/// the others in its group, those sharing its <see cref="GroupName"/> under the same root, or with none, those sharing
/// its parent. The built-in look is a ring with a dot while checked, beside its content; a template replaces it (a
/// segmented row of toggles is a <c>CheckStates</c> template).
/// </summary>
public sealed class RadioButton : ToggleButton
{
	private static readonly SolidColorBrush RingStroke = Brush.Sealed(HdrColor.FromRgb(160, 160, 170));
	private static readonly SolidColorBrush RingFill = Brush.Sealed(HdrColor.FromRgb(40, 40, 48));
	private static readonly SolidColorBrush RingHover = Brush.Sealed(HdrColor.FromRgb(58, 58, 70));
	private static readonly SolidColorBrush DotFill = Brush.Sealed(HdrColor.FromRgb(80, 150, 240));
	public static readonly DependencyProperty<RadioButton, string?> GroupNameProperty =
		DependencyProperty.Register(nameof(GroupName), new PropertyMetadata<RadioButton, string?>());

	private Border? _ring;
	private Border? _dot;
	private Brush _accent = DotFill;

	public RadioButton() => SetBuiltInValue(TemplateProperty, DefaultTemplate);

	/// <summary>The name of the group it is one of; null for the buttons sharing its parent.</summary>
	public string? GroupName { get => GetValue(GroupNameProperty); set => SetValue(GroupNameProperty, value); }

	/// <summary>A radio button is only ever checked by a click.</summary>
	protected override void OnToggle()
	{
		if (IsChecked != true)
			IsChecked = true;
	}

	protected override void OnToggleStateChanged()
	{
		if (_ring is not null && _dot is not null)
		{
			_dot.Visibility = IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
			_ring.BorderBrush = IsChecked == true ? _accent : RingStroke;
		}
		if (IsChecked == true)
			UncheckOthers();
	}

	private void UncheckOthers()
	{
		if (GroupName is null)
		{
			if (Parent is Panel siblings)
			{
				foreach (var sibling in siblings.Children.Items)
				{
					if (sibling is RadioButton { GroupName: null } other && !ReferenceEquals(other, this))
						other.IsChecked = false;
				}
			}
			return;
		}
		if (AttachedRoot?.Content is { } top)
			Uncheck(top);
	}

	private void Uncheck(UIElement element)
	{
		if (element is RadioButton other && !ReferenceEquals(other, this) && other.GroupName == GroupName)
			other.IsChecked = false;
		foreach (var child in element.VisualChildren)
			Uncheck(child);
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var radio = (RadioButton)control;
			radio._accent = radio.ThemeResource("RadioButtonOuterEllipseCheckedStroke", DotFill);
			var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			var ring = new Border
			{
				Width = 18f,
				Height = 18f,
				CornerRadius = new CornerRadius(9f),
				Background = RingFill,
				BorderBrush = RingStroke,
				BorderThickness = new Thickness(1.5f),
				VerticalAlignment = VerticalAlignment.Center,
				BackgroundTransition = TemplateStates.Fade(120),
			};
			var dot = new Border
			{
				Width = 8f,
				Height = 8f,
				CornerRadius = new CornerRadius(4f),
				Background = radio._accent,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			ring.Child = dot;
			var presenter = new ContentPresenter { Margin = new Thickness(8f, 0f), VerticalAlignment = VerticalAlignment.Center };
			radio.RegisterTemplateBinding(BackgroundProperty, row, Panel.BackgroundProperty);
			radio.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			radio.RegisterContentTemplateBinding(content => presenter.Content = content);
			row.Children.Add(ring);
			row.Children.Add(presenter);
			(radio._ring, radio._dot) = (ring, dot);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(ring, RingHover) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(ring, RingFill) });
			common.States.Add(new VisualState
			{
				Name = "Disabled",
				Storyboard = TemplateStates.Hold((dot, Border.BackgroundProperty, TemplateStates.DisabledAccent),
					(ring, Border.BackgroundProperty, TemplateStates.DisabledBackground),
					(ring, Border.BorderBrushProperty, TemplateStates.DisabledBorder),
					(presenter, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground)),
			});
			VisualStateManager.SetVisualStateGroups(row, [common]);
			radio.OnToggleStateChanged();
			return row;
		},
	};
}
