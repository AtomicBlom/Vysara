namespace SUIsei.Controls;

/// <summary>
/// A <see cref="ToggleButton"/> with a check-box look (PlanningUI.md MU6): a square box hosting an inner fill glyph,
/// beside a <see cref="ContentControl.Content"/> label. The glyph is hidden when unchecked, the accent fill when
/// checked, and a dimmer fill when indeterminate. (A drawn check mark awaits a glyph in the font atlas; the filled
/// box is the current representation.)
/// </summary>
public sealed class CheckBox : ToggleButton
{
	private static readonly HdrColor BoxBorder = HdrColor.FromRgb(160, 160, 170);
	private static readonly HdrColor BoxBackground = HdrColor.FromRgb(40, 40, 48);
	private static readonly HdrColor CheckFill = HdrColor.FromRgb(80, 150, 240);
	private static readonly HdrColor IndeterminateFill = HdrColor.FromRgb(120, 120, 130);

	private Border? _checkGlyph;

	public CheckBox() => Template = DefaultTemplate;

	protected override void OnToggleStateChanged()
	{
		if (_checkGlyph is null)
			return;
		_checkGlyph.Visibility = IsChecked == false ? Visibility.Collapsed : Visibility.Visible;
		_checkGlyph.Background = IsChecked is null ? IndeterminateFill : CheckFill;
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

			// Reflect the initial state as soon as the parts exist.
			checkBox.OnToggleStateChanged();
			return row;
		},
	};
}
