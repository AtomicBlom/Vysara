using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A set of choices of which one is chosen, as WinUI's <c>RadioButtons</c>: a <see cref="RadioButton"/> per item of
/// <see cref="ItemsSource"/> (or <see cref="Items"/>), shown through <see cref="ItemTemplate"/> or as text, down
/// <see cref="MaxColumns"/> columns, under an optional <see cref="Header"/>. Choosing one sets
/// <see cref="SelectedIndex"/> and <see cref="SelectedItem"/>, dependency properties with change events, so they bind
/// TwoWay; the arrow keys (and the d-pad) move the choice from the focused button.
/// </summary>
public sealed class RadioButtons : Control
{
	public static readonly DependencyProperty<RadioButtons, int> SelectedIndexProperty =
		DependencyProperty.Register(nameof(SelectedIndex),
			new PropertyMetadata<RadioButtons, int> { DefaultValue = -1, PropertyChanged = static (r, _, _) => r.OnSelectedIndexChanged() });

	public static readonly DependencyProperty<RadioButtons, object?> SelectedItemProperty =
		DependencyProperty.Register(nameof(SelectedItem),
			new PropertyMetadata<RadioButtons, object?> { PropertyChanged = static (r, _, _) => r.OnSelectedItemChanged() });

	private readonly List<RadioButton> _buttons = [];
	private readonly Grid _grid = new();
	private readonly StackPanel _column = new() { Orientation = Orientation.Vertical };
	private readonly ContentPresenter _header = new() { Margin = new Thickness(0f, 0f, 0f, 6f), Visibility = Visibility.Collapsed };
	private bool _syncing;

	public RadioButtons()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		_column.Children.Add(_header);
		_column.Children.Add(_grid);
		Items.CollectionChanged += (_, _) => Rebuild();
		AddHandler(KeyDownEvent, OnKeyDown);
		SetBuiltInValue(TemplateProperty, new ControlTemplate { Factory = static control => ((RadioButtons)control)._column });
	}

	/// <summary>The choices where no <see cref="ItemsSource"/> is set.</summary>
	public ObservableCollection<object?> Items { get; } = [];

	/// <summary>The choices; replaces <see cref="Items"/> while set. One that raises collection changes is followed.</summary>
	public IEnumerable? ItemsSource
	{
		get;
		set
		{
			if (field is INotifyCollectionChanged old)
				old.CollectionChanged -= OnSourceChanged;
			field = value;
			if (value is INotifyCollectionChanged @new)
				@new.CollectionChanged += OnSourceChanged;
			Rebuild();
		}
	}

	/// <summary>How each choice is shown; null shows it as text.</summary>
	public DataTemplate? ItemTemplate { get; set { field = value; Rebuild(); } }

	/// <summary>How many columns the choices fill, left to right then down. 1 by default.</summary>
	public int MaxColumns { get; set { field = Math.Max(1, value); Rebuild(); } } = 1;

	/// <summary>Shown above the choices, or nothing where null.</summary>
	public object? Header
	{
		get;
		set
		{
			field = value;
			_header.Content = value;
			_header.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
		}
	}

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

	private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

	private void Rebuild()
	{
		foreach (var button in _buttons)
			button.IsCheckedChanged -= OnButtonChecked;
		_buttons.Clear();
		_grid.Children.Clear();
		_grid.ColumnDefinitions.Clear();
		_grid.RowDefinitions.Clear();
		var items = Choices.Cast<object?>().ToList();
		var columns = Math.Min(MaxColumns, Math.Max(1, items.Count));
		for (var c = 0; c < columns; c++)
			_grid.ColumnDefinitions.Add(GridLength.Auto);
		for (var r = 0; r < (items.Count + columns - 1) / columns; r++)
			_grid.RowDefinitions.Add(GridLength.Auto);
		for (var i = 0; i < items.Count; i++)
		{
			var button = new RadioButton
			{
				Content = ItemTemplate is { } template ? template.Factory(items[i]) : items[i],
				Margin = new Thickness(0f, 0f, 12f, 4f),
			};
			Grid.SetColumn(button, i % columns);
			Grid.SetRow(button, i / columns);
			button.IsCheckedChanged += OnButtonChecked;
			_buttons.Add(button);
			_grid.Children.Add(button);
		}
		// The choice is kept where its item is still there
		var keep = SelectedItem is { } item ? items.IndexOf(item) : -1;
		Show(keep);
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
			Show(SelectedIndex);
		SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectedItemChanged()
	{
		if (!_syncing)
			Show(SelectedItem is { } item ? Choices.Cast<object?>().ToList().IndexOf(item) : -1);
		SelectedItemChanged?.Invoke(this, EventArgs.Empty);
	}

	// The choice at `index` (or none) checked, the properties set, SelectionChanged raised where it moved
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
				_buttons[i].IsChecked = i == index;
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
			VirtualKey.Up or VirtualKey.GamepadDPadUp => -MaxColumns,
			VirtualKey.Down or VirtualKey.GamepadDPadDown => MaxColumns,
			VirtualKey.Left or VirtualKey.GamepadDPadLeft => -1,
			VirtualKey.Right or VirtualKey.GamepadDPadRight => 1,
			_ => 0,
		};
		var target = at + step;
		if (step == 0 || target < 0 || target >= _buttons.Count)
			return;
		e.Handled = true;
		Show(target);
		_buttons[target].Focus(FocusState.Keyboard);
	}
}
