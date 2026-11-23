using System.Collections;
using System.Collections.ObjectModel;
using Vysara.Controls.Primitives;
using Vysara.Input;
using Vysara.RoutedEvents;

namespace Vysara.Controls;

/// <summary>
/// A set of choices of which one is chosen, as WinUI's <c>RadioButtons</c>: a <see cref="RadioButton"/> per item of
/// <see cref="ItemsSource"/> (or <see cref="Items"/>), shown through <see cref="ItemTemplate"/> or as text, under an
/// optional <see cref="Header"/>, in up to <see cref="MaxColumns"/> columns filled top to bottom, then left to right, as
/// WinUI's. Choosing one sets <see cref="SelectedIndex"/> and <see cref="SelectedItem"/>, dependency properties with
/// change events, so they bind TwoWay. The set is one tab stop, the chosen button (the first while none is chosen); from
/// it Up and Down (and the d-pad's) move the choice to the item before or after, and Left and Right to the next column.
/// Items added before it is in a live tree, as markup adds them, are laid out once it is.
/// </summary>
public sealed class RadioButtons : Control
{
	public static readonly DependencyProperty<RadioButtons, int> SelectedIndexProperty =
		DependencyProperty.Register(nameof(SelectedIndex),
			new PropertyMetadata<RadioButtons, int> { DefaultValue = -1, PropertyChanged = static (r, _, _) => r.OnSelectedIndexChanged() });

	public static readonly DependencyProperty<RadioButtons, object?> SelectedItemProperty =
		DependencyProperty.Register(nameof(SelectedItem),
			new PropertyMetadata<RadioButtons, object?> { PropertyChanged = static (r, _, _) => r.OnSelectedItemChanged() });

	public static readonly DependencyProperty<RadioButtons, IEnumerable?> ItemsSourceProperty =
		DependencyProperty.Register(nameof(ItemsSource),
			new PropertyMetadata<RadioButtons, IEnumerable?> { PropertyChanged = static (r, _, source) => r.OnItemsSourceChanged(source) });

	public static readonly DependencyProperty<RadioButtons, DataTemplate?> ItemTemplateProperty =
		DependencyProperty.Register(nameof(ItemTemplate),
			new PropertyMetadata<RadioButtons, DataTemplate?> { PropertyChanged = static (r, _, _) => r.ItemsChanged() });

	public static readonly DependencyProperty<RadioButtons, int> MaxColumnsProperty =
		DependencyProperty.Register(nameof(MaxColumns),
			new PropertyMetadata<RadioButtons, int>
			{
				DefaultValue = 1,
				Coerce = static (_, columns) => Math.Max(1, columns),
				PropertyChanged = static (r, _, _) => r.ItemsChanged(),
			});

	public static readonly DependencyProperty<RadioButtons, object?> HeaderProperty =
		DependencyProperty.Register(nameof(Header),
			new PropertyMetadata<RadioButtons, object?> { PropertyChanged = static (r, _, _) => r.ShowHeader() });

	private readonly List<RadioButton> _buttons = [];
	private readonly SourceSubscription _source;
	private Grid? _grid;
	private ContentPresenter? _header;
	// The buttons no longer match the items, and are built again before they are next read
	private bool _stale = true;
	// How many rows the buttons fill, each column top to bottom
	private int _rows = 1;
	private bool _syncing;

	public RadioButtons()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		_source = new SourceSubscription((_, _) => ItemsChanged(), ItemsChanged);
		Items.CollectionChanged += (_, _) => ItemsChanged();
		AddHandler(KeyDownEvent, OnKeyDown);
	}

	/// <summary>The choices where no <see cref="ItemsSource"/> is set.</summary>
	public ObservableCollection<object?> Items { get; } = [];

	/// <summary>The choices; replaces <see cref="Items"/> while set. One that raises collection changes is followed.</summary>
	public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

	/// <summary>How each choice is shown; null shows it as text.</summary>
	public DataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

	/// <summary>How many columns the choices fill, each top to bottom. 1 by default.</summary>
	public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

	/// <summary>Shown above the choices, or nothing where null.</summary>
	public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

	/// <summary>The index of the choice made, or -1.</summary>
	public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

	/// <summary>The choice made, or null.</summary>
	public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

	/// <summary>Raised as the choice changes.</summary>
	public event SelectionChangedEventHandler? SelectionChanged;

	/// <summary>Raised as <see cref="SelectedIndex"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? SelectedIndexChanged;

	/// <summary>Raised as <see cref="SelectedItem"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? SelectedItemChanged;

	private IEnumerable Choices => ItemsSource ?? Items;

	private protected override void OnLiveTreeChanged(bool attached)
	{
		base.OnLiveTreeChanged(attached);
		_source.LiveTreeChanged(attached);
	}

	private void OnItemsSourceChanged(IEnumerable? source)
	{
		_source.Source = source;
		ItemsChanged();
	}

	// The items or how they show changed: built again now in a live tree, else once the buttons are next needed, so
	// markup adding items one by one builds them once
	private void ItemsChanged()
	{
		_stale = true;
		InvalidateMeasure();
		if (AttachedRoot is not null)
			EnsureBuilt();
	}

	private void EnsureBuilt()
	{
		if (_grid is null)
			ApplyTemplate(); // which builds them, where the built-in template is the one
		if (!_stale || _grid is null)
			return;
		_stale = false;
		foreach (var button in _buttons)
			button.IsCheckedChanged -= OnButtonChecked;
		_buttons.Clear();
		_grid.Children.Clear();
		_grid.ColumnDefinitions.Clear();
		_grid.RowDefinitions.Clear();
		var items = Choices.Cast<object?>().ToList();
		var columns = Math.Min(MaxColumns, Math.Max(1, items.Count));
		_rows = Math.Max(1, (items.Count + columns - 1) / columns);
		for (var c = 0; c < columns; c++)
			_grid.ColumnDefinitions.Add(GridLength.Auto);
		for (var r = 0; r < _rows; r++)
			_grid.RowDefinitions.Add(GridLength.Auto);
		for (var i = 0; i < items.Count; i++)
		{
			var button = new RadioButton
			{
				Content = ItemTemplate is { } template ? template.Factory(items[i]) : items[i],
				Margin = new Thickness(0f, 0f, 12f, 4f),
			};
			Grid.SetColumn(button, i / _rows);
			Grid.SetRow(button, i % _rows);
			button.IsCheckedChanged += OnButtonChecked;
			_buttons.Add(button);
			_grid.Children.Add(button);
		}
		// The choice is kept where its item is still there, or made where an index was set before the buttons were
		Show(SelectedItem is { } item ? items.IndexOf(item) : SelectedIndex);
	}

	private void ShowHeader()
	{
		if (_header is null)
			return;
		_header.Content = Header;
		_header.Visibility = Header is null ? Visibility.Collapsed : Visibility.Visible;
	}

	protected override Size MeasureOverride(Size available)
	{
		EnsureBuilt();
		return base.MeasureOverride(available);
	}

	// A button checked by input makes its item the choice
	private void OnButtonChecked(object? sender, EventArgs e)
	{
		if (_syncing || sender is not RadioButton { IsChecked: true } button)
			return;
		Show(_buttons.IndexOf(button));
	}

	private void OnSelectedIndexChanged()
	{
		if (!_syncing)
		{
			EnsureBuilt();
			if (_grid is not null)
			{
				var index = SelectedIndex;
				Show(index);
				if (SelectedIndex != index)
					return; // an index with no item became -1, whose change raised the event already
			}
		}
		SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectedItemChanged()
	{
		if (!_syncing)
		{
			EnsureBuilt();
			if (_grid is not null)
				Show(SelectedItem is { } item ? Choices.Cast<object?>().ToList().IndexOf(item) : -1);
		}
		SelectedItemChanged?.Invoke(this, EventArgs.Empty);
	}

	// The choice at `index` (or none) checked, the properties set, SelectionChanged raised where it moved; the chosen
	// button (or the first) is the set's one tab stop
	private void Show(int index)
	{
		if (index < 0 || index >= _buttons.Count)
			index = -1;
		var removed = SelectedItem;
		var item = index >= 0 ? Choices.Cast<object?>().ElementAt(index) : null;
		var moved = index != SelectedIndex || !Equals(item, removed);
		_syncing = true;
		try
		{
			for (var i = 0; i < _buttons.Count; i++)
			{
				_buttons[i].IsChecked = i == index;
				_buttons[i].IsTabStop = i == Math.Max(0, index);
			}
			SelectedIndex = index;
			SelectedItem = item;
		}
		finally
		{
			_syncing = false;
		}
		if (moved && SelectionChanged is { } handler)
			handler(this, new SelectionChangedEventArgs(removed is null ? [] : [removed], item is null ? [] : [item]) { OriginalSource = this, Source = this });
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key || key.OriginalSource is not RadioButton from || _buttons.IndexOf(from) is var at && at < 0)
			return;
		var step = key.Key switch
		{
			VirtualKey.Up or VirtualKey.GamepadDPadUp => -1,
			VirtualKey.Down or VirtualKey.GamepadDPadDown => 1,
			VirtualKey.Left or VirtualKey.GamepadDPadLeft => -_rows,
			VirtualKey.Right or VirtualKey.GamepadDPadRight => _rows,
			_ => 0,
		};
		var target = at + step;
		if (step == 0 || target < 0 || target >= _buttons.Count)
			return;
		e.Handled = true;
		Show(target);
		_buttons[target].Focus(FocusState.Keyboard);
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (RadioButtons)control;
			var header = new ContentPresenter { Margin = new Thickness(0f, 0f, 0f, 6f), Visibility = Visibility.Collapsed };
			self.RegisterTemplateBinding(ForegroundProperty, header, ContentPresenter.ForegroundProperty);
			var grid = new Grid();
			var column = new StackPanel { Orientation = Orientation.Vertical };
			column.Children.Add(header);
			column.Children.Add(grid);
			(self._grid, self._header) = (grid, header);
			self._stale = true;
			self.ShowHeader();
			self.EnsureBuilt();
			return column;
		},
	};
}
