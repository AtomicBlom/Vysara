using SUIsei.Animation;
using SUIsei.Controls.Primitives;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A header that opens and closes the content under it, as WinUI's <c>Expander</c>: the header row (its
/// <see cref="Header"/> and a chevron, down while closed and up while open) is clicked, or Enter, Space or the pad's A
/// pressed on it, to flip <see cref="IsExpanded"/>, which shows or collapses <see cref="ContentControl.Content"/> below.
/// <see cref="Expanding"/> and <see cref="Collapsed"/> say which way it went; <see cref="IsExpandedChanged"/> lets it bind
/// TwoWay. The header takes <c>ExpanderHeaderBackground</c> and the content <c>ExpanderContentBackground</c>.
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

	private readonly Pressable _press;
	private Border? _content;
	private Grid? _chevrons;
	private ContentPresenter? _header;

	public Expander()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		InheritedText.GiveBuiltInLook(this);
		_press = new Pressable(this, () => IsExpanded = !IsExpanded, () => GoToCommonState(useTransitions: true));
	}

	/// <summary>Whether the content shows.</summary>
	public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

	/// <summary>What the header row shows.</summary>
	public object? Header { get; set { field = value; Refresh(); } }

	/// <summary>Raised as the expander opens.</summary>
	public event EventHandler? Expanding;

	/// <summary>Raised as the expander closes.</summary>
	public event EventHandler? Collapsed;

	/// <summary>Raised as <see cref="IsExpanded"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? IsExpandedChanged;

	private protected override string CommonVisualState => _press.State(IsEnabled);

	private protected override void OnIsEnabledChanged(bool isEnabled) => _press.Reset();

	private void OnIsExpandedChanged()
	{
		Refresh();
		(IsExpanded ? Expanding : Collapsed)?.Invoke(this, EventArgs.Empty);
		IsExpandedChanged?.Invoke(this, EventArgs.Empty);
	}

	private void Refresh()
	{
		if (_content is null || _chevrons is null || _header is null)
			return;
		_content.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
		_chevrons.Children[0].Visibility = IsExpanded ? Visibility.Collapsed : Visibility.Visible;
		_chevrons.Children[1].Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
		_header.Content = Header;
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
			chevrons.Children.Add(Marks.ChevronDown(10f, ChevronStroke));
			chevrons.Children.Add(Marks.ChevronUp(10f, ChevronStroke));
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
			(self._content, self._chevrons, self._header) = (content, chevrons, header);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(headerRow, HeaderOver) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(headerRow, HeaderOver) });
			common.States.Add(new VisualState
			{
				Name = "Disabled",
				Storyboard = TemplateStates.Hold((header, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground)),
			});
			VisualStateManager.SetVisualStateGroups(column, [common]);
			self.Refresh();
			return column;
		},
	};
}
