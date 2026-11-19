using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// An <see cref="ItemsControl"/> that wraps each item in a selectable <see cref="ListViewItem"/>, as WinUI's. A click or
/// tap on an item selects it, and so does moving through the list with the keyboard or a pad from an item that has focus:
/// Up and Down (the d-pad's too), Home, End, Page Up and Page Down, passing over items whose content is collapsed (a
/// filtered list). <see cref="SelectedIndex"/> and <see cref="SelectedItem"/> are dependency properties with change
/// events, so they bind both ways. With <see cref="IsItemClickEnabled"/>, a click, Enter, Space or the pad's A on an item
/// raises <see cref="ItemClick"/>. Single selection; moving the keyboard through a virtualizing panel reaches only the
/// items it has realized.
/// </summary>
public class ListView : ItemsControl
{
	// How many items Page Up and Page Down move.
	private const int PageStep = 10;

	private ListViewItem? _selected;
	// True while the list sets its own selection properties, so their change callbacks don't select again.
	private bool _syncing;

	public ListView()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		AddHandler(KeyDownEvent, OnKeyDown);
	}

	public static readonly DependencyProperty<ListView, int> SelectedIndexProperty =
		DependencyProperty.Register(nameof(SelectedIndex),
			new PropertyMetadata<ListView, int> { DefaultValue = -1, PropertyChanged = static (l, _, index) => l.OnSelectedIndexChanged(index) });

	public static readonly DependencyProperty<ListView, object?> SelectedItemProperty =
		DependencyProperty.Register(nameof(SelectedItem),
			new PropertyMetadata<ListView, object?> { PropertyChanged = static (l, _, item) => l.OnSelectedItemChanged(item) });

	/// <summary>How many items may be selected. Defaults to <see cref="SelectionMode.Single"/>.</summary>
	public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;

	/// <summary>When true, a click, Enter, Space or the pad's A on an item raises <see cref="ItemClick"/>.</summary>
	public bool IsItemClickEnabled { get; set; }

	/// <summary>The index of the selected item in <see cref="ItemsControl.Items"/>, or -1. Setting it selects that item;
	/// an index with no item clears the selection.</summary>
	public int SelectedIndex
	{
		get => GetValue(SelectedIndexProperty);
		set => SetValue(SelectedIndexProperty, value);
	}

	/// <summary>The selected data item, or null. Setting it selects the item equal to it; one not in the list is kept,
	/// and selected if it is there when the items are next loaded. A reload keeps the selection where the selected item is
	/// still in the list.</summary>
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
		var container = new ListViewItem { Content = inner, DataItem = item };
		container.AddHandler(PointerPressedEvent, (_, _) => SelectContainer(container));
		container.AddHandler(PointerReleasedEvent, (_, _) => Invoke(container));
		return container;
	}

	protected override void OnReload()
	{
		var removed = _selected?.DataItem;
		var hadSelection = _selected is not null;
		var wanted = SelectedItem;
		_selected = null;
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

	// The property moved, set by the list or from outside: from outside it selects; either way its event is raised
	private void OnSelectedIndexChanged(int index)
	{
		if (!_syncing)
			SelectIndex(index);
		SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnSelectedItemChanged(object? item)
	{
		if (!_syncing)
		{
			if (item is null)
				ClearSelection();
			else if (Find(item) is { } container)
				SelectContainer(container);
		}
		SelectedItemChanged?.Invoke(this, EventArgs.Empty);
	}

	private void Invoke(ListViewItem container)
	{
		if (IsItemClickEnabled && ItemClick is { } handler)
			handler(this, new ItemClickEventArgs(container.DataItem) { OriginalSource = container, Source = this });
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key || ItemsHost is not { } host || host.Children.Count == 0)
			return;
		var from = key.OriginalSource is UIElement source ? ContainerOf(source) : null;
		if (from is null)
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

	private ListViewItem? ContainerOf(UIElement element)
	{
		for (var el = element; el is not null && !ReferenceEquals(el, this); el = el.Parent)
		{
			if (el is ListViewItem item && ReferenceEquals(item.Parent, ItemsHost))
				return item;
		}
		return null;
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
