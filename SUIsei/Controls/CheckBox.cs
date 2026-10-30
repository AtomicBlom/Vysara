using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A <see cref="ToggleButton"/> with a check-box look: a square box hosting an inner fill glyph,
/// beside a <see cref="ContentControl.Content"/> label. The glyph is hidden when unchecked, the accent fill when
/// checked, and a dimmer fill when indeterminate, and the box border picks up the accent while (in)determinate. The
/// box itself reacts to the pointer through a <c>CommonStates</c> group (hover lightens it, press darkens it). As in
/// WinUI's default template, the row behind box and label paints the check box's <see cref="Control.Background"/>, and
/// the label its <see cref="Control.Foreground"/>; the box keeps its own look.
/// (A drawn check mark awaits a glyph in the font atlas; the filled box is the representation used.)
/// </summary>
public sealed class CheckBox : ToggleButton
{
	// The built-in look's brushes, shared by every check box and never changed.
	private static readonly SolidColorBrush BoxBorder = Brush.Sealed(HdrColor.FromRgb(160, 160, 170));
	private static readonly SolidColorBrush BoxBackground = Brush.Sealed(HdrColor.FromRgb(40, 40, 48));
	private static readonly SolidColorBrush BoxBackgroundHover = Brush.Sealed(HdrColor.FromRgb(58, 58, 70));
	private static readonly SolidColorBrush BoxBackgroundPressed = Brush.Sealed(HdrColor.FromRgb(30, 30, 38));
	private static readonly SolidColorBrush CheckFill = Brush.Sealed(HdrColor.FromRgb(80, 150, 240));
	private static readonly SolidColorBrush IndeterminateFill = Brush.Sealed(HdrColor.FromRgb(120, 120, 130));

	private Border? _box;
	private Border? _checkGlyph;

	public CheckBox() => SetBuiltInValue(TemplateProperty, DefaultTemplate);

	protected override void OnToggleStateChanged()
	{
		if (_box is null || _checkGlyph is null)
			return;
		var state = IsChecked;
		_checkGlyph.Visibility = state == false ? Visibility.Collapsed : Visibility.Visible;
		_checkGlyph.Background = state is null ? IndeterminateFill : CheckFill;
		// The box border picks up the accent once there's something to show, so the checked affordance reads even at a
		// glance (and the hover/press background still shows underneath via CommonStates).
		_box.BorderBrush = state == false ? BoxBorder : CheckFill;
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var checkBox = (CheckBox)control;
			var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

			var box = new Border
			{
				Width = 18f,
				Height = 18f,
				CornerRadius = new CornerRadius(3f),
				Background = BoxBackground,
				BorderBrush = BoxBorder,
				BorderThickness = new Thickness(1.5f),
				VerticalAlignment = VerticalAlignment.Center,
				BackgroundTransition = TemplateStates.Fade(120),
			};
			var glyph = new Border { Background = CheckFill, CornerRadius = new CornerRadius(1.5f), Margin = new Thickness(3f) };
			box.Child = glyph;
			checkBox._box = box;
			checkBox._checkGlyph = glyph;

			var presenter = new ContentPresenter
			{
				Margin = new Thickness(8f, 0f),
				VerticalAlignment = VerticalAlignment.Center,
			};
			checkBox.RegisterTemplateBinding(BackgroundProperty, row, Panel.BackgroundProperty);
			checkBox.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			checkBox.RegisterContentTemplateBinding(content => presenter.Content = content);

			row.Children.Add(box);
			row.Children.Add(presenter);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(box, BoxBackgroundHover) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(box, BoxBackgroundPressed) });
			common.States.Add(new VisualState { Name = "Disabled", Storyboard = TemplateStates.Dimmed(row) });
			VisualStateManager.SetVisualStateGroups(row, [common]);

			// Reflect the initial checked state as soon as the parts exist.
			checkBox.OnToggleStateChanged();
			return row;
		},
	};
}
