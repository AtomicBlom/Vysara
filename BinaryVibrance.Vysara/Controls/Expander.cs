using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Controls.Primitives;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Shapes;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// A header that opens and closes the content beside it, as WinUI's <c>Expander</c>: the header row (its
/// <see cref="Header"/> and a chevron pointing the way it will open) is clicked, or Enter, Space or the pad's A pressed
/// while the expander has focus, to flip <see cref="IsExpanded"/>, which shows or collapses
/// <see cref="ContentControl.Content"/> below the header, or above it with <see cref="ExpandDirection"/> <c>Up</c>.
/// Input in the content is the content's own: a click or a key there never flips it. <see cref="Expanding"/> and
/// <see cref="Collapsed"/> say which way it went; <see cref="IsExpandedChanged"/> lets it bind TwoWay. The header takes
/// <c>ExpanderHeaderBackground</c> and the content <c>ExpanderContentBackground</c>.
/// </summary>
public sealed class Expander : ContentControl
{
	private static readonly SolidColorBrush HeaderBackground = Brush.Sealed(HdrColor.FromRgb(40, 42, 52));
	private static readonly SolidColorBrush HeaderOver = Brush.Sealed(HdrColor.FromRgb(50, 53, 66));
	private static readonly SolidColorBrush ContentBackground = Brush.Sealed(HdrColor.FromRgb(32, 34, 42));
	private static readonly SolidColorBrush ChevronStroke = Brush.Sealed(HdrColor.FromRgb(170, 176, 190));

	public static readonly DependencyProperty<Expander, bool> IsExpandedProperty =
		DependencyProperty.Register(nameof(IsExpanded),
			new PropertyMetadata<Expander, bool> { PropertyChanged = static (e, _, _) => e.OnIsExpandedChanged() });

	public static readonly DependencyProperty<Expander, object?> HeaderProperty =
		DependencyProperty.Register(nameof(Header),
			new PropertyMetadata<Expander, object?> { PropertyChanged = static (e, _, _) => e.Refresh() });

	public static readonly DependencyProperty<Expander, ExpandDirection> ExpandDirectionProperty =
		DependencyProperty.Register(nameof(ExpandDirection),
			new PropertyMetadata<Expander, ExpandDirection> { PropertyChanged = static (e, _, _) => e.Refresh() });

	private readonly Pressable _press;
	private StackPanel? _column;
	private Border? _headerRow;
	private Border? _content;
	private Grid? _chevrons;
	private ContentPresenter? _header;

	public Expander()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		InheritedText.GiveBuiltInLook(this);
		// Only the header row takes presses; it is given to the press handling as the template builds it
		_press = new Pressable(this, () => IsExpanded = !IsExpanded, () => GoToCommonState(useTransitions: true), wholeControl: false);
	}

	/// <summary>Whether the content shows.</summary>
	public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

	/// <summary>What the header row shows.</summary>
	public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

	/// <summary>Which way the content opens from the header: down (the default) or up.</summary>
	public ExpandDirection ExpandDirection { get => GetValue(ExpandDirectionProperty); set => SetValue(ExpandDirectionProperty, value); }

	/// <summary>Raised as the expander opens.</summary>
	public event TypedEventHandler<Expander, ExpanderExpandingEventArgs>? Expanding;

	/// <summary>Raised as the expander has closed.</summary>
	public event TypedEventHandler<Expander, ExpanderCollapsedEventArgs>? Collapsed;

	/// <summary>Raised as <see cref="IsExpanded"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? IsExpandedChanged;

	private protected override string CommonVisualState => _press.State(IsEnabled);

	private protected override void OnIsEnabledChanged(bool isEnabled) => _press.Reset();

	private void OnIsExpandedChanged()
	{
		Refresh();
		if (IsExpanded)
			Expanding?.Invoke(this, ExpanderExpandingEventArgs.Instance);
		else
			Collapsed?.Invoke(this, ExpanderCollapsedEventArgs.Instance);
		IsExpandedChanged?.Invoke(this, EventArgs.Empty);
	}

	private void Refresh()
	{
		if (_column is null || _headerRow is null || _content is null || _chevrons is null || _header is null)
			return;
		var up = ExpandDirection == ExpandDirection.Up;
		_content.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
		// The chevron points the way the content will go: towards it while closed, back towards the header while open
		var pointsDown = up ? IsExpanded : !IsExpanded;
		_chevrons.Children[0].Visibility = pointsDown ? Visibility.Visible : Visibility.Collapsed;
		_chevrons.Children[1].Visibility = pointsDown ? Visibility.Collapsed : Visibility.Visible;
		_header.Content = Header;
		if (ReferenceEquals(_column.Children[0], _headerRow) == up)
		{
			_column.Children.Clear();
			_column.Children.Add(up ? _content : _headerRow);
			_column.Children.Add(up ? _headerRow : _content);
		}
		_content.CornerRadius = up ? new CornerRadius(6f, 6f, 0f, 0f) : new CornerRadius(0f, 0f, 6f, 6f);
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (Expander)control;
			var column = new StackPanel { Orientation = Orientation.Vertical };
			var headerRow = new Border
			{
				Background = self.ThemeResource("ExpanderHeaderBackground", HeaderBackground),
				CornerRadius = new CornerRadius(6f),
				Padding = new Thickness(12f, 8f, 12f, 8f),
				BackgroundTransition = TemplateStates.Fade(100),
			};
			var grid = new Grid();
			grid.ColumnDefinitions.Add(GridLength.Star);
			grid.ColumnDefinitions.Add(GridLength.Auto);
			var header = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
			var chevrons = new Grid { Margin = new Thickness(12f, 0f, 0f, 0f) };
			var down = Marks.ChevronDown(10f, ChevronStroke);
			var upMark = Marks.ChevronUp(10f, ChevronStroke);
			chevrons.Children.Add(down);
			chevrons.Children.Add(upMark);
			Grid.SetColumn(chevrons, 1);
			grid.Children.Add(header);
			grid.Children.Add(chevrons);
			headerRow.Child = grid;
			var content = new Border
			{
				Background = self.ThemeResource("ExpanderContentBackground", ContentBackground),
				CornerRadius = new CornerRadius(0f, 0f, 6f, 6f),
				Padding = new Thickness(12f, 10f, 12f, 12f),
			};
			var presenter = new ContentPresenter();
			content.Child = presenter;
			self.RegisterContentTemplateBinding(value => presenter.Content = value);
			self.RegisterTemplateBinding(ForegroundProperty, header, ContentPresenter.ForegroundProperty);
			column.Children.Add(headerRow);
			column.Children.Add(content);
			(self._column, self._headerRow, self._content, self._chevrons, self._header) = (column, headerRow, content, chevrons, header);
			self._press.Area = headerRow;

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(headerRow, HeaderOver) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(headerRow, HeaderOver) });
			common.States.Add(new VisualState
			{
				Name = "Disabled",
				Storyboard = TemplateStates.Hold(
					(headerRow, Border.BackgroundProperty, TemplateStates.DisabledBackground),
					(header, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground),
					(down, Shape.StrokeProperty, TemplateStates.DisabledForeground),
					(upMark, Shape.StrokeProperty, TemplateStates.DisabledForeground)),
			});
			VisualStateManager.SetVisualStateGroups(column, [common]);
			self.Refresh();
			return column;
		},
	};
}
