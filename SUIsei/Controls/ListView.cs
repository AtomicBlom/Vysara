namespace SUIsei.Controls;

/// <summary>
/// An <see cref="ItemsControl"/> that wraps each item in a selectable <see cref="ListViewItem"/>.
/// Clicking an item selects it (driving the item's <c>SelectionStates</c> visual); single selection is supported
/// (full multi-select is a later refinement).
/// </summary>
public class ListView : ItemsControl
{
	private ListViewItem? _selected;

	/// <summary>How many items may be selected. Defaults to <see cref="SelectionMode.Single"/>.</summary>
	public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;

	/// <summary>The index of the selected item in <see cref="ItemsControl.Items"/>, or -1.</summary>
	public int SelectedIndex { get; private set; } = -1;

	/// <summary>The selected data item, or null.</summary>
	public object? SelectedItem { get; private set; }

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
		return container;
	}

	protected override void OnReload()
	{
		_selected = null;
		SelectedItem = null;
		SelectedIndex = -1;
	}

	internal void SelectContainer(ListViewItem container)
	{
		if (SelectionMode == SelectionMode.None || ReferenceEquals(_selected, container))
			return;

		_selected?.SetSelected(false);
		_selected = container;
		container.SetSelected(true);
		SelectedItem = container.DataItem;
		SelectedIndex = ItemsHost is { } host ? IndexOf(host, container) : -1;
	}

	private void ClearSelection()
	{
		_selected?.SetSelected(false);
		_selected = null;
		SelectedItem = null;
		SelectedIndex = -1;
	}

	private static int IndexOf(Panel host, UIElement container)
	{
		for (var i = 0; i < host.Children.Count; i++)
			if (ReferenceEquals(host.Children[i], container))
				return i;
		return -1;
	}
}
