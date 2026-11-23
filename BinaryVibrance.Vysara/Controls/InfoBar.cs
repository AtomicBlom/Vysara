using BinaryVibrance.Vysara.Controls.Primitives;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// A message in the page rather than over it, as WinUI's <c>InfoBar</c>: its <see cref="Title"/> and
/// <see cref="Message"/> on a panel edged in its <see cref="Severity"/>'s colour, an optional <see cref="ActionButton"/>,
/// and a close button while <see cref="IsClosable"/>. It shows while <see cref="IsOpen"/>. However it closes, by its close
/// button (which raises <see cref="CloseButtonClick"/> first) or by <see cref="IsOpen"/> set false, it raises
/// <see cref="Closing"/>, which may keep it open, then <see cref="Closed"/>, each with the reason.
/// <see cref="IsOpenChanged"/> lets it bind TwoWay. The panel takes <c>InfoBarInformationalSeverityBackgroundBrush</c>,
/// <c>InfoBarSuccessSeverityBackgroundBrush</c>, <c>InfoBarWarningSeverityBackgroundBrush</c> or
/// <c>InfoBarErrorSeverityBackgroundBrush</c> by its severity, and its edge the <c>InfoBar*SeverityIconBackground</c> ones.
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
			new PropertyMetadata<InfoBar, bool> { PropertyChanged = static (b, _, open) => b.OnIsOpenChanged(open) });

	public static readonly DependencyProperty<InfoBar, string?> TitleProperty =
		DependencyProperty.Register(nameof(Title), new PropertyMetadata<InfoBar, string?> { PropertyChanged = static (b, _, _) => b.Refresh() });

	public static readonly DependencyProperty<InfoBar, string?> MessageProperty =
		DependencyProperty.Register(nameof(Message), new PropertyMetadata<InfoBar, string?> { PropertyChanged = static (b, _, _) => b.Refresh() });

	public static readonly DependencyProperty<InfoBar, InfoBarSeverity> SeverityProperty =
		DependencyProperty.Register(nameof(Severity), new PropertyMetadata<InfoBar, InfoBarSeverity> { PropertyChanged = static (b, _, _) => b.Refresh() });

	public static readonly DependencyProperty<InfoBar, bool> IsClosableProperty =
		DependencyProperty.Register(nameof(IsClosable),
			new PropertyMetadata<InfoBar, bool> { DefaultValue = true, PropertyChanged = static (b, _, _) => b.Refresh() });

	public static readonly DependencyProperty<InfoBar, ButtonBase?> ActionButtonProperty =
		DependencyProperty.Register(nameof(ActionButton), new PropertyMetadata<InfoBar, ButtonBase?> { PropertyChanged = static (b, _, _) => b.Refresh() });

	private Border? _panel;
	private Border? _edge;
	private TextBlock? _title;
	private TextBlock? _message;
	private ContentPresenter? _action;
	private Button? _close;
	// Each severity's panel and edge, read from the page's resources as the template is built
	private (Brush Panel, Brush Edge)[] _looks = [];
	// Set while the bar closes itself, so the IsOpen change it makes is not taken as a programmatic close
	private bool _closing;

	public InfoBar()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		InheritedText.GiveBuiltInLook(this);
	}

	/// <summary>Whether the bar shows.</summary>
	public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

	/// <summary>The bar's title, a size up, over the message.</summary>
	public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

	/// <summary>What the bar says.</summary>
	public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

	/// <summary>How much it matters, which colours its panel and edge.</summary>
	public InfoBarSeverity Severity { get => GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

	/// <summary>Whether a close button shows. True by default.</summary>
	public bool IsClosable { get => GetValue(IsClosableProperty); set => SetValue(IsClosableProperty, value); }

	/// <summary>A button shown after the message, or none.</summary>
	public ButtonBase? ActionButton { get => GetValue(ActionButtonProperty); set => SetValue(ActionButtonProperty, value); }

	/// <summary>Raised as the close button is clicked, before the bar closes.</summary>
	public event TypedEventHandler<InfoBar, object>? CloseButtonClick;

	/// <summary>Raised as the bar is about to close; setting <see cref="InfoBarClosingEventArgs.Cancel"/> keeps it open.</summary>
	public event TypedEventHandler<InfoBar, InfoBarClosingEventArgs>? Closing;

	/// <summary>Raised as the bar has closed, by its close button or by <see cref="IsOpen"/> set false.</summary>
	public event TypedEventHandler<InfoBar, InfoBarClosedEventArgs>? Closed;

	/// <summary>Raised as <see cref="IsOpen"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? IsOpenChanged;

	private void OnIsOpenChanged(bool open)
	{
		if (!open && !_closing)
		{
			// Set false from code: it may still be kept open
			var closing = new InfoBarClosingEventArgs(InfoBarCloseReason.Programmatic);
			Closing?.Invoke(this, closing);
			if (closing.Cancel)
			{
				IsOpen = true;
				return;
			}
		}
		Refresh();
		IsOpenChanged?.Invoke(this, EventArgs.Empty);
		if (!open && !_closing)
			Closed?.Invoke(this, new InfoBarClosedEventArgs(InfoBarCloseReason.Programmatic));
	}

	private void OnClose(object sender, RoutedEventArgs e)
	{
		CloseButtonClick?.Invoke(this, EventArgs.Empty);
		var closing = new InfoBarClosingEventArgs(InfoBarCloseReason.CloseButton);
		Closing?.Invoke(this, closing);
		if (closing.Cancel)
			return;
		_closing = true;
		try
		{
			IsOpen = false;
		}
		finally
		{
			_closing = false;
		}
		Closed?.Invoke(this, new InfoBarClosedEventArgs(InfoBarCloseReason.CloseButton));
	}

	private void Refresh()
	{
		if (_panel is null || _edge is null || _title is null || _message is null || _action is null || _close is null)
			return;
		_panel.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
		var look = _looks[(int)Severity is >= 0 and <= 3 ? (int)Severity : 0];
		_panel.Background = look.Panel;
		_edge.Background = look.Edge;
		_title.Text = Title ?? "";
		_title.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
		_message.Text = Message ?? "";
		_action.Content = ActionButton;
		_action.Visibility = ActionButton is null ? Visibility.Collapsed : Visibility.Visible;
		_close.Visibility = IsClosable ? Visibility.Visible : Visibility.Collapsed;
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (InfoBar)control;
			// In InfoBarSeverity's order: Informational, Success, Warning, Error
			self._looks =
			[
				(self.ThemeResource("InfoBarInformationalSeverityBackgroundBrush", Panel), self.ThemeResource("InfoBarInformationalSeverityIconBackground", Informational)),
				(self.ThemeResource("InfoBarSuccessSeverityBackgroundBrush", Panel), self.ThemeResource("InfoBarSuccessSeverityIconBackground", Success)),
				(self.ThemeResource("InfoBarWarningSeverityBackgroundBrush", Panel), self.ThemeResource("InfoBarWarningSeverityIconBackground", Warning)),
				(self.ThemeResource("InfoBarErrorSeverityBackgroundBrush", Panel), self.ThemeResource("InfoBarErrorSeverityIconBackground", Error)),
			];
			var panel = new Border { CornerRadius = new CornerRadius(6f) };
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
