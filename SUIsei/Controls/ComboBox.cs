using System.Collections;
using System.Collections.ObjectModel;
using SUIsei.Animation;
using SUIsei.Controls.Primitives;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// One choice from a list that drops down, as WinUI's <c>ComboBox</c>: a box showing <see cref="SelectedItem"/> (through
/// <see cref="ItemTemplate"/>, or as text; <see cref="PlaceholderText"/> while there is none) and a chevron, under an
/// optional <see cref="Header"/>. A click, Enter, Space, F4, Alt+Down or the pad's A opens the list of
/// <see cref="ItemsSource"/> (or <see cref="Items"/>) below it, at least as wide as the box, the choice focused; a click,
/// Enter or Space there chooses, Escape or a press outside closes it. Closed, Up and Down step the choice.
/// <see cref="SelectedIndex"/> and <see cref="SelectedItem"/> are dependency properties with change events, so they bind
/// TwoWay. The box takes the <c>ComboBoxBackground</c>, <c>ComboBoxBackgroundPointerOver</c> and
/// <c>ComboBoxBackgroundPressed</c> resources.
/// </summary>
public sealed class ComboBox : Control
{
	private static readonly SolidColorBrush BoxBackground = Brush.Sealed(HdrColor.FromRgb(60, 60, 70));
	private static readonly SolidColorBrush BoxOver = Brush.Sealed(HdrColor.FromRgb(80, 80, 95));
	private static readonly SolidColorBrush BoxPressed = Brush.Sealed(HdrColor.FromRgb(40, 40, 55));
	private static readonly SolidColorBrush ChevronStroke = Brush.Sealed(HdrColor.FromRgb(170, 176, 190));
	private static readonly SolidColorBrush Placeholder = Brush.Sealed(HdrColor.FromRgb(140, 146, 160));

	public static readonly DependencyProperty<ComboBox, int> SelectedIndexProperty =
		DependencyProperty.Register(nameof(SelectedIndex),
			new PropertyMetadata<ComboBox, int> { DefaultValue = -1, PropertyChanged = static (c, _, _) => c.OnSelectedIndexChanged() });

	public static readonly DependencyProperty<ComboBox, object?> SelectedItemProperty =
		DependencyProperty.Register(nameof(SelectedItem),
			new PropertyMetadata<ComboBox, object?> { PropertyChanged = static (c, _, _) => c.OnSelectedItemChanged() });

	private readonly Pressable _press;
	private readonly DropDown _dropDown;
	private ContentPresenter? _shown;
	private ContentPresenter? _header;
	private bool _syncing;

	public ComboBox()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		InheritedText.GiveBuiltInLook(this);
		_dropDown = new DropDown(this);
		_press = new Pressable(this, Open, () => GoToCommonState(useTransitions: true));
		AddHandler(KeyDownEvent, OnKeyDown);
		Items.CollectionChanged += (_, _) => Reconcile();
	}

	/// <summary>The choices where no <see cref="ItemsSource"/> is set.</summary>
	public ObservableCollection<object?> Items { get; } = [];

	/// <summary>The choices; replaces <see cref="Items"/> while set.</summary>
	public IEnumerable? ItemsSource { get; set { field = value; Reconcile(); } }

	/// <summary>How each choice is shown, in the box and the list; null shows it as text.</summary>
	public DataTemplate? ItemTemplate { get; set { field = value; ShowChoice(); } }

	/// <summary>Shown in the box while nothing is chosen.</summary>
	public string? PlaceholderText { get; set { field = value; ShowChoice(); } }

	/// <summary>Shown above the box, or nothing where null.</summary>
	public object? Header { get; set { field = value; ShowChoice(); } }

	/// <summary>Whether the list is open.</summary>
	public bool IsDropDownOpen => _dropDown.IsOpen;

	/// <summary>The index of the choice, or -1.</summary>
	public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

	/// <summary>The choice, or null.</summary>
	public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

	/// <summary>Raised as the choice changes.</summary>
	public event SelectionChangedEventHandler? SelectionChanged;

	/// <summary>Raised as <see cref="SelectedIndex"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? SelectedIndexChanged;

	/// <summary>Raised as <see cref="SelectedItem"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? SelectedItemChanged;

	/// <summary>Raised as the list opens.</summary>
	public event EventHandler<object>? DropDownOpened;

	/// <summary>Raised as the list closes.</summary>
	public event EventHandler<object>? DropDownClosed;

	private protected override string CommonVisualState => _press.State(IsEnabled);

	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		_press.Reset();
		_dropDown.Hide();
	}

	internal List<object?> Choices() => (ItemsSource ?? Items).Cast<object?>().ToList();

	/// <summary>Opens the list.</summary>
	public void Open()
	{
		if (!IsEnabled || _dropDown.IsOpen)
			return;
		_dropDown.ShowAt(this);
		DropDownOpened?.Invoke(this, EventArgs.Empty);
	}

	internal void OnDropDownClosed()
	{
		if (FocusManager.IsFocusable(this))
			Focus(FocusState.Programmatic);
		DropDownClosed?.Invoke(this, EventArgs.Empty);
	}

	// The items changed: the choice is kept where its item is still there
	private void Reconcile()
	{
		var choices = Choices();
		Choose(SelectedItem is { } item ? choices.IndexOf(item) : -1, choices);
	}

	private void OnSelectedIndexChanged()
	{
		if (!_syncing)
			Choose(SelectedIndex, Choices());
		SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectedItemChanged()
	{
		if (!_syncing)
		{
			var choices = Choices();
			Choose(SelectedItem is { } item ? choices.IndexOf(item) : -1, choices);
		}
		SelectedItemChanged?.Invoke(this, EventArgs.Empty);
	}

	internal void Choose(int index, List<object?> choices)
	{
		if (index < 0 || index >= choices.Count)
			index = -1;
		var removed = SelectedItem;
		var item = index >= 0 ? choices[index] : null;
		var moved = index != SelectedIndex || !Equals(item, removed);
		_syncing = true;
		try
		{
			SelectedIndex = index;
			SelectedItem = item;
		}
		finally
		{
			_syncing = false;
		}
		ShowChoice();
		if (moved && SelectionChanged is { } handler)
			handler(this, new SelectionChangedEventArgs(removed is null ? [] : [removed], item is null ? [] : [item]) { OriginalSource = this, Source = this });
	}

	private void ShowChoice()
	{
		if (_shown is null || _header is null)
			return;
		_header.Content = Header;
		_header.Visibility = Header is null ? Visibility.Collapsed : Visibility.Visible;
		if (SelectedIndex < 0)
		{
			_shown.Content = new TextBlock { Text = PlaceholderText ?? "", Foreground = Placeholder };
			return;
		}
		_shown.Content = ItemTemplate is { } template ? template.Factory(SelectedItem) : SelectedItem?.ToString() ?? "";
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key || !ReferenceEquals(key.OriginalSource, this))
			return;
		var alt = (key.Modifiers & ModifierKeys.Alt) != 0;
		switch (key.Key)
		{
			case VirtualKey.F4:
			case VirtualKey.Down when alt:
				Open();
				break;
			case VirtualKey.Up or VirtualKey.GamepadDPadUp:
				Choose(Math.Max(0, SelectedIndex - 1), Choices());
				break;
			case VirtualKey.Down or VirtualKey.GamepadDPadDown:
				var choices = Choices();
				Choose(Math.Min(choices.Count - 1, SelectedIndex + 1), choices);
				break;
			default: return;
		}
		e.Handled = true;
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (ComboBox)control;
			var column = new StackPanel { Orientation = Orientation.Vertical };
			var header = new ContentPresenter { Margin = new Thickness(0f, 0f, 0f, 6f) };
			var box = new Border
			{
				CornerRadius = new CornerRadius(4f),
				Padding = new Thickness(10f, 5f, 8f, 5f),
				BackgroundTransition = TemplateStates.Fade(120),
			};
			var background = self.ThemeResource("ComboBoxBackground", BoxBackground);
			var over = self.ThemeResource("ComboBoxBackgroundPointerOver", BoxOver);
			var pressed = self.ThemeResource("ComboBoxBackgroundPressed", BoxPressed);
			self.RegisterTemplateBinding(BackgroundProperty, box, Border.BackgroundProperty, value => value ?? background);
			var grid = new Grid();
			grid.ColumnDefinitions.Add(GridLength.Star);
			grid.ColumnDefinitions.Add(GridLength.Auto);
			var shown = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
			var chevron = Marks.ChevronDown(10f, ChevronStroke);
			chevron.Margin = new Thickness(10f, 0f, 0f, 0f);
			Grid.SetColumn(chevron, 1);
			grid.Children.Add(shown);
			grid.Children.Add(chevron);
			box.Child = grid;
			self.RegisterTemplateBinding(ForegroundProperty, shown, ContentPresenter.ForegroundProperty);
			self.RegisterTemplateBinding(ForegroundProperty, header, ContentPresenter.ForegroundProperty);
			column.Children.Add(header);
			column.Children.Add(box);
			(self._shown, self._header) = (shown, header);

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(box, over) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(box, pressed) });
			common.States.Add(new VisualState
			{
				Name = "Disabled",
				Storyboard = TemplateStates.Hold((box, Border.BackgroundProperty, TemplateStates.DisabledBackground),
					(shown, ContentPresenter.ForegroundProperty, TemplateStates.DisabledForeground)),
			});
			VisualStateManager.SetVisualStateGroups(column, [common]);
			self.ShowChoice();
			return column;
		},
	};

	// The list a combo box drops down: its choices in a list view as wide as the box at least, the choice focused
	private sealed class DropDown : FlyoutBase
	{
		private static readonly SolidColorBrush ListBackground = Brush.Sealed(HdrColor.FromRgb(45, 45, 55));
		private static readonly SolidColorBrush ListBorder = Brush.Sealed(HdrColor.FromRgb(80, 80, 95));

		private readonly ComboBox _owner;
		private ListView? _list;

		public DropDown(ComboBox owner)
		{
			_owner = owner;
			Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
		}

		protected override float Gap => 2f;

		protected override FrameworkElement CreatePresenter()
		{
			var choices = _owner.Choices();
			var list = new ListView { ItemsSource = choices, ItemTemplate = _owner.ItemTemplate, IsItemClickEnabled = true, SelectedIndex = _owner.SelectedIndex };
			list.ItemClick += (_, e) =>
			{
				_owner.Choose(choices.IndexOf(e.ClickedItem), choices);
				Hide();
			};
			_list = list;
			return new Border
			{
				Background = Resource(_owner, "ComboBoxDropDownBackground", ListBackground),
				BorderBrush = Resource(_owner, "ComboBoxDropDownBorderBrush", ListBorder),
				BorderThickness = new Thickness(1f),
				CornerRadius = new CornerRadius(4f),
				Padding = new Thickness(3f),
				MinWidth = _owner.LayoutRect.Width,
				Child = new ScrollViewer { Content = list },
			};
		}

		protected override void OnOpened()
		{
			// The choice, rather than the first item, has the focus
			if (_list is { SelectedIndex: >= 0 and var index, ItemsHost: { } host } && index < host.Children.Count)
				host.Children[index].Focus(FocusState.Programmatic);
		}

		protected override void OnClosed()
		{
			_list = null;
			_owner.OnDropDownClosed();
		}
	}
}
