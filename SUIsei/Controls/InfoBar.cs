using SUIsei.Controls.Primitives;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A message in the page rather than over it, as WinUI's <c>InfoBar</c>: its <see cref="Title"/> and
/// <see cref="Message"/> on a panel edged in its <see cref="Severity"/>'s colour, an optional <see cref="ActionButton"/>,
/// and a close button while <see cref="IsClosable"/>. It shows while <see cref="IsOpen"/>; the close button sets it false
/// and raises <see cref="Closed"/>. <see cref="IsOpenChanged"/> lets it bind TwoWay. The panel takes
/// <c>InfoBarInformationalSeverityBackgroundBrush</c> and its siblings for Success, Warning and Error.
/// </summary>
public sealed class InfoBar : Control
{
	private static readonly SolidColorBrush Panel = Brush.Sealed(HdrColor.FromRgb(36, 38, 48));
	private static readonly SolidColorBrush Informational = Brush.Sealed(HdrColor.FromRgb(96, 160, 240));
	private static readonly SolidColorBrush Success = Brush.Sealed(HdrColor.FromRgb(96, 200, 128));
	private static readonly SolidColorBrush Warning = Brush.Sealed(HdrColor.FromRgb(232, 176, 72));
	private static readonly SolidColorBrush Error = Brush.Sealed(HdrColor.FromRgb(224, 92, 92));
	private static readonly SolidColorBrush CrossStroke = Brush.Sealed(HdrColor.FromRgb(170, 176, 190));

	public static readonly DependencyProperty<InfoBar, bool> IsOpenProperty =
		DependencyProperty.Register(nameof(IsOpen),
			new PropertyMetadata<InfoBar, bool> { PropertyChanged = static (b, _, _) => b.OnIsOpenChanged() });

	private Border? _panel;
	private Border? _edge;
	private TextBlock? _title;
	private TextBlock? _message;
	private ContentPresenter? _action;
	private Button? _close;

	public InfoBar()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		InheritedText.GiveBuiltInLook(this);
	}

	/// <summary>Whether the bar shows.</summary>
	public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

	/// <summary>The bar's title, a size up, over the message.</summary>
	public string? Title { get; set { field = value; Refresh(); } }

	/// <summary>What the bar says.</summary>
	public string? Message { get; set { field = value; Refresh(); } }

	/// <summary>How much it matters, which colours its edge.</summary>
	public InfoBarSeverity Severity { get; set { field = value; Refresh(); } }

	/// <summary>Whether a close button shows. True by default.</summary>
	public bool IsClosable { get; set { field = value; Refresh(); } } = true;

	/// <summary>A button shown after the message, or none.</summary>
	public ButtonBase? ActionButton { get; set { field = value; Refresh(); } }

	/// <summary>Raised as the close button closes the bar.</summary>
	public event EventHandler? Closed;

	/// <summary>Raised as <see cref="IsOpen"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? IsOpenChanged;

	private void OnIsOpenChanged()
	{
		Refresh();
		IsOpenChanged?.Invoke(this, EventArgs.Empty);
	}

	private void Refresh()
	{
		if (_panel is null || _edge is null || _title is null || _message is null || _action is null || _close is null)
			return;
		_panel.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
		_edge.Background = Severity switch
		{
			InfoBarSeverity.Success => ThemeResource("InfoBarSuccessSeverityIconBackground", Success),
			InfoBarSeverity.Warning => ThemeResource("InfoBarWarningSeverityIconBackground", Warning),
			InfoBarSeverity.Error => ThemeResource("InfoBarErrorSeverityIconBackground", Error),
			_ => ThemeResource("InfoBarInformationalSeverityIconBackground", Informational),
		};
		_title.Text = Title ?? "";
		_title.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
		_message.Text = Message ?? "";
		_action.Content = ActionButton;
		_action.Visibility = ActionButton is null ? Visibility.Collapsed : Visibility.Visible;
		_close.Visibility = IsClosable ? Visibility.Visible : Visibility.Collapsed;
	}

	private void OnClose(object sender, RoutedEventArgs e)
	{
		IsOpen = false;
		Closed?.Invoke(this, EventArgs.Empty);
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (InfoBar)control;
			var panel = new Border
			{
				Background = self.ThemeResource("InfoBarInformationalSeverityBackgroundBrush", Panel),
				CornerRadius = new CornerRadius(6f),
			};
			var grid = new Grid();
			grid.ColumnDefinitions.Add(GridLength.Pixels(4f));
			grid.ColumnDefinitions.Add(GridLength.Star);
			grid.ColumnDefinitions.Add(GridLength.Auto);
			grid.ColumnDefinitions.Add(GridLength.Auto);
			var edge = new Border { CornerRadius = new CornerRadius(6f, 0f, 0f, 6f) };
			var words = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12f, 8f, 8f, 8f), VerticalAlignment = VerticalAlignment.Center };
			var title = new TextBlock { FontSize = 15f };
			var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
			words.Children.Add(title);
			words.Children.Add(message);
			var action = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4f, 0f) };
			var close = new Button { Content = Marks.Cross(9f, CrossStroke), Padding = new Thickness(8f), Margin = new Thickness(4f), VerticalAlignment = VerticalAlignment.Center };
			ToolTipService.SetToolTip(close, "Close");
			close.Click += self.OnClose;
			Grid.SetColumn(words, 1);
			Grid.SetColumn(action, 2);
			Grid.SetColumn(close, 3);
			grid.Children.Add(edge);
			grid.Children.Add(words);
			grid.Children.Add(action);
			grid.Children.Add(close);
			panel.Child = grid;
			self.RegisterTemplateBinding(ForegroundProperty, title, TextBlock.ForegroundProperty);
			self.RegisterTemplateBinding(ForegroundProperty, message, TextBlock.ForegroundProperty);
			(self._panel, self._edge, self._title, self._message, self._action, self._close) = (panel, edge, title, message, action, close);
			self.Refresh();
			return panel;
		},
	};
}
