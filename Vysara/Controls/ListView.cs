using Vysara.Input;
using Vysara.RoutedEvents;

namespace Vysara.Controls;

/// <summary>
/// An <see cref="ItemsControl"/> that wraps each item in a selectable <see cref="ListViewItem"/>, in a
/// <see cref="ScrollViewer"/> of its own, as WinUI's. A primary click or tap on an item selects it, and so does moving
/// through the list with the keyboard or a pad from an item that has focus: Up and Down (the d-pad's too), Home, End, Page
/// Up and Page Down, passing over items whose content is collapsed (a filtered list); keys a control inside an item
/// handles (a text box's Home and End) stay with it. <see cref="SelectedIndex"/> and <see cref="SelectedItem"/> are
/// dependency properties with change events, so they bind both ways. With <see cref="IsItemClickEnabled"/>, a click (a
/// press and release on the same item), Enter, Space or the pad's A on an item raises <see cref="ItemClick"/>. Single
/// selection. A list laid out by a <see cref="VirtualizingStackPanel"/> has no scroll viewer, as that panel scrolls itself,
/// and moving the keyboard through it reaches only the items it has realized.
/// </summary>
public class ListView : ItemsControl
{
	// How many items Page Up and Page Down move.
	private const int PageStep = 10;

	private ListViewItem? _selected;
	// The item a primary press went down on, which a release over it clicks.
	private ListViewItem? _pressed;
	// True while the list sets its own selection properties, so their change callbacks don't select again.
	private bool _syncing;

	public ListView() => SetBuiltInValue(IsTabStopProperty, false);

	public static readonly DependencyProperty<ListView, int> SelectedIndexProperty =
		DependencyProperty.Register(nameof(SelectedIndex),
			new PropertyMetadata<ListView, int> { DefaultValue = -1, PropertyChanged = static (l, _, index) => l.OnSelectedIndexChanged(index) });

	public static readonly DependencyProperty<ListView, object?> SelectedItemProperty =
		DependencyProperty.Register(nameof(SelectedItem),
			new PropertyMetadata<ListView, object?> { PropertyChanged = static (l, old, item) => l.OnSelectedItemChanged(old, item) });

	public static readonly DependencyProperty<ListView, SelectionMode> SelectionModeProperty =
		DependencyProperty.Register(nameof(SelectionMode), new PropertyMetadata<ListView, SelectionMode> { DefaultValue = SelectionMode.Single });

	public static readonly DependencyProperty<ListView, bool> IsItemClickEnabledProperty =
		DependencyProperty.Register(nameof(IsItemClickEnabled), new PropertyMetadata<ListView, bool>());

	/// <summary>How many items may be selected. Defaults to <see cref="SelectionMode.Single"/>.</summary>
	public SelectionMode SelectionMode { get => GetValue(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }

	/// <summary>When true, a click, Enter, Space or the pad's A on an item raises <see cref="ItemClick"/>.</summary>
	public bool IsItemClickEnabled { get => GetValue(IsItemClickEnabledProperty); set => SetValue(IsItemClickEnabledProperty, value); }

	/// <summary>The index of the selected item in <see cref="ItemsControl.Items"/>, or -1. Setting it selects that item;
	/// an index with no item clears the selection.</summary>
	public int SelectedIndex
	{
		get => GetValue(SelectedIndexProperty);
		set => SetValue(SelectedIndexProperty, value);
	}

	/// <summary>The selected data item, or null. Setting it selects the item equal to it. While the list has no items, one
	/// set is kept, and selected if it is there when the items are loaded (markup may set the selection before the items);
	/// once it has items, setting one not among them is ignored, as WinUI ignores it. A reload keeps the selection where the
	/// selected item is still in the list.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>Raised when the selection changes, by a click, the keyboard, a property or <see cref="SelectIndex"/>, or
	/// when reloading the items clears it.</summary>
	public event SelectionChangedEventHandler? SelectionChanged;

	/// <summary>Raised when <see cref="SelectedIndex"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? SelectedIndexChanged;

	/// <summary>Raised when <see cref="SelectedItem"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? SelectedItemChanged;

	/// <summary>Raised, with <see cref="IsItemClickEnabled"/>, when an item is clicked or invoked from the keyboard.</summary>
	public event ItemClickEventHandler? ItemClick;

	private protected override bool ScrollsItems => true;

	/// <summary>Select the item at <paramref name="index"/> (or clear with -1).</summary>
	public void SelectIndex(int index)
	{
		if (index < 0 || index >= (ItemsHost?.Children.Count ?? 0))
		{
			ClearSelection();
			return;
		}
		if (ItemsHost!.Children[index] is ListViewItem container)
			SelectContainer(container);
	}

	protected override UIElement RealizeContainer(object? item)
	{
		var inner = ItemTemplate is { } dt ? (UIElement)dt.Factory(item) : ContentHelper.Wrap(item) ?? new TextBlock();
		var container = new ListViewItem { Content = inner, DataItem = item, Owner = this };
		container.AddHandler(PointerPressedEvent, (_, e) => OnItemPressed(container, e));
		container.AddHandler(PointerReleasedEvent, (_, e) => OnItemReleased(container, e));
		container.AddHandler(PointerCaptureLostEvent, (_, _) => _pressed = null);
		// On the item, so a control inside it handles its own keys first and the list's scroll viewer sees only the rest
		container.AddHandler(KeyDownEvent, OnItemKeyDown);
		return container;
	}

	protected override void OnReload()
	{
		var removed = _selected?.DataItem;
		var hadSelection = _selected is not null;
		var wanted = SelectedItem;
		_selected = null;
		_pressed = null;
		if (wanted is not null && Find(wanted) is { } again)
		{
			_selected = again;
			again.SetSelected(true);
			Sync(IndexOf(ItemsHost!, again), again.DataItem);
			if (!hadSelection || !Equals(removed, again.DataItem))
				RaiseSelectionChanged(hadSelection, removed, true, again.DataItem);
			return;
		}
		if (!hadSelection)
			return;
		Sync(-1, null);
		RaiseSelectionChanged(true, removed, false, null);
	}

	// The container of the item equal to `item`, or null
	private ListViewItem? Find(object item)
	{
		if (ItemsHost is not { } host)
			return null;
		for (var i = 0; i < host.Children.Count; i++)
		{
			if (host.Children[i] is ListViewItem container && Equals(container.DataItem, item))
				return container;
		}
		return null;
	}

	internal void SelectContainer(ListViewItem container)
	{
		if (SelectionMode == SelectionMode.None || ReferenceEquals(_selected, container))
			return;

		var previous = _selected;
		var removed = previous?.DataItem;
		previous?.SetSelected(false);
		_selected = container;
		container.SetSelected(true);
		Sync(ItemsHost is { } host ? IndexOf(host, container) : -1, container.DataItem);
		RaiseSelectionChanged(previous is not null, removed, true, container.DataItem);
	}

	// An item's IsSelected set from outside: selecting it selects it here, and unselecting the selected one clears it
	internal void OnItemIsSelectedChanged(ListViewItem container, bool selected)
	{
		if (selected)
		{
			SelectContainer(container);
			if (!ReferenceEquals(_selected, container))
				container.SetSelected(false); // the list takes no selection
		}
		else if (ReferenceEquals(_selected, container))
			ClearSelection();
	}

	private void ClearSelection()
	{
		if (_selected is not { } previous)
		{
			Sync(-1, null);
			return;
		}
		var removed = previous.DataItem;
		previous.SetSelected(false);
		_selected = null;
		Sync(-1, null);
		RaiseSelectionChanged(true, removed, false, null);
	}

	// The selection properties set to what is selected, without selecting again
	private void Sync(int index, object? item)
	{
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
	}

	// The property moved, set by the list or from outside: from outside it selects; either way its event is raised once.
	// An index with no item becomes -1, whose change raised the event already.
	private void OnSelectedIndexChanged(int index)
	{
		if (!_syncing)
		{
			SelectIndex(index);
			if (SelectedIndex != index)
				return;
		}
		SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectedItemChanged(object? old, object? item)
	{
		if (!_syncing)
		{
			if (item is null)
				ClearSelection();
			else if (Find(item) is { } container)
				SelectContainer(container);
			else if (ItemsHost is { Children.Count: > 0 })
			{
				// Not among the items: ignored, and the selection shown again
				Sync(SelectedIndex, _selected?.DataItem);
				return;
			}
		}
		SelectedItemChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnItemPressed(ListViewItem container, RoutedEventArgs e)
	{
		if (e is PointerRoutedEventArgs { IsSecondaryButton: true })
			return;
		_pressed = container;
		SelectContainer(container);
	}

	// A release clicks the item the press went down on, while the pointer is still over it: not one it was dragged to or
	// off. The pressed item holds the pointer, so the release comes to it wherever it is.
	private void OnItemReleased(ListViewItem container, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs { IsSecondaryButton: false } pointer)
			return;
		var pressed = _pressed;
		_pressed = null;
		var at = pointer.GetCurrentPoint(container).Position;
		var over = at.X >= 0f && at.Y >= 0f && at.X < container.LayoutRect.Width && at.Y < container.LayoutRect.Height;
		if (ReferenceEquals(pressed, container) && over)
			Invoke(container);
	}

	private void Invoke(ListViewItem container)
	{
		if (IsItemClickEnabled && ItemClick is { } handler)
			handler(this, new ItemClickEventArgs(container.DataItem) { OriginalSource = container, Source = this });
	}

	private void OnItemKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key || sender is not ListViewItem from || ItemsHost is not { } host || host.Children.Count == 0)
			return;
		var at = IndexOf(host, from);
		int target;
		switch (key.Key)
		{
			case VirtualKey.Up or VirtualKey.GamepadDPadUp: target = Step(host, at, -1, 1); break;
			case VirtualKey.Down or VirtualKey.GamepadDPadDown: target = Step(host, at, 1, 1); break;
			case VirtualKey.PageUp: target = Step(host, at, -1, PageStep); break;
			case VirtualKey.PageDown: target = Step(host, at, 1, PageStep); break;
			case VirtualKey.Home: target = Step(host, -1, 1, 1); break;
			case VirtualKey.End: target = Step(host, host.Children.Count, -1, 1); break;
			case VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA:
				SelectContainer(from);
				Invoke(from);
				e.Handled = true;
				return;
			default: return;
		}
		e.Handled = true;
		if (target < 0 || target == at || host.Children[target] is not ListViewItem next)
			return;
		SelectContainer(next);
		next.Focus(FocusState.Keyboard);
	}

	// The item `count` shown items on from `from` in `direction`, or the last shown one short of the end; -1 for none
	private static int Step(Panel host, int from, int direction, int count)
	{
		var found = -1;
		for (int i = from + direction, moved = 0; i >= 0 && i < host.Children.Count && moved < count; i += direction)
		{
			if (host.Children[i] is ListViewItem { ShowsContent: true })
			{
				found = i;
				moved++;
			}
		}
		return found;
	}

	// Raise SelectionChanged from this list, with at most one item on each side, as single selection changes them. Nothing
	// is made when no one listens, and an empty side is the shared empty array.
	private void RaiseSelectionChanged(bool hasRemoved, object? removed, bool hasAdded, object? added)
	{
		if (SelectionChanged is not { } handler)
			return;
		var args = new SelectionChangedEventArgs(hasRemoved ? [removed] : Array.Empty<object?>(), hasAdded ? [added] : Array.Empty<object?>())
		{
			OriginalSource = this,
			Source = this,
		};
		handler(this, args);
	}

	private static int IndexOf(Panel host, UIElement container)
	{
		for (var i = 0; i < host.Children.Count; i++)
			if (ReferenceEquals(host.Children[i], container))
				return i;
		return -1;
	}
}
