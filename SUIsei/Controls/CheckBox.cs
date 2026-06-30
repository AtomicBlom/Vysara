namespace SUIsei.Controls;

/// <summary>
/// A <see cref="ToggleButton"/> with a check-box look: a square box hosting an inner fill glyph,
/// beside a <see cref="ContentControl.Content"/> label. The glyph is hidden when unchecked, the accent fill when
/// checked, and a dimmer fill when indeterminate, and the box border picks up the accent while (in)determinate. The
/// box itself reacts to the pointer through a <c>CommonStates</c> group (hover lightens it, press darkens it).
/// (A drawn check mark awaits a glyph in the font atlas; the filled box is the representation used.)
/// </summary>
public sealed class CheckBox : ToggleButton
{
	private static readonly HdrColor BoxBorder = HdrColor.FromRgb(160, 160, 170);
	private static readonly HdrColor BoxBackground = HdrColor.FromRgb(40, 40, 48);
	private static readonly HdrColor BoxBackgroundHover = HdrColor.FromRgb(58, 58, 70);
	private static readonly HdrColor BoxBackgroundPressed = HdrColor.FromRgb(30, 30, 38);
	private static readonly HdrColor CheckFill = HdrColor.FromRgb(80, 150, 240);
	private static readonly HdrColor IndeterminateFill = HdrColor.FromRgb(120, 120, 130);

	private Border? _box;
	private Border? _checkGlyph;

	public CheckBox() => Template = DefaultTemplate;

	protected override void OnToggleStateChanged()
	{
		if (_box is null || _checkGlyph is null)
			return;
		var state = IsChecked;
		_checkGlyph.Visibility = state == false ? Visibility.Collapsed : Visibility.Visible;
		_checkGlyph.Background = state is null ? IndeterminateFill : CheckFill;
		// The box border picks up the accent once there's something to show, so the checked affordance reads even at a
		// glance (and the hover/press background still animates underneath via CommonStates).
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
				CornerRadius = 3f,
				Background = BoxBackground,
				BorderBrush = BoxBorder,
				BorderThickness = new Thickness(1.5f),
				VerticalAlignment = VerticalAlignment.Center,
			};
			var glyph = new Border { Background = CheckFill, CornerRadius = 1.5f, Margin = new Thickness(3f) };
			box.Child = glyph;
			checkBox._box = box;
			checkBox._checkGlyph = glyph;

			var presenter = new ContentPresenter
			{
				Margin = new Thickness(8f, 0f),
				VerticalAlignment = VerticalAlignment.Center,
			};
			presenter.Content = checkBox.Content;
			checkBox.ContentChanged += (_, _) => presenter.Content = checkBox.Content;

			row.Children.Add(box);
			row.Children.Add(presenter);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = BoxFillTo(box, BoxBackgroundHover, BoxBackground) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = BoxFillTo(box, BoxBackground, BoxBackgroundHover) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = BoxFillTo(box, BoxBackgroundHover, BoxBackgroundPressed) });
			VisualStateManager.SetVisualStateGroups(row, [common]);

			// Reflect the initial checked state as soon as the parts exist.
			checkBox.OnToggleStateChanged();
			return row;
		},
	};

	// A short background-colour transition on the box, on the no-relayout visual path.
	private static Storyboard BoxFillTo(Border box, HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.HoldEnd,
				Apply = box.SetAnimatedFill, Release = box.ClearAnimatedFill,
			},
		},
	};
}
