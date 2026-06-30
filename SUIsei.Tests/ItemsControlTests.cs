using System.Collections.ObjectModel;
using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// Items-control coverage: one container realized per item (through a data template or the
/// default wrap), incremental updates from an <c>ObservableCollection</c> (add/remove/replace/reset), and ListView
/// single selection driving the item's selection visual. Headless — no GPU.
/// </summary>
public sealed class ItemsControlTests
{
	[Test]
	public void Items_control_realizes_one_container_per_item_via_the_data_template()
	{
		var ic = new ItemsControl
		{
			ItemTemplate = new DataTemplate { Factory = i => new TextBlock { Text = i?.ToString() ?? "" } },
			ItemsSource = new[] { "a", "b", "c" },
		};

		ic.ItemsHost!.Children.Count.ShouldBe(3);
		((TextBlock)ic.ItemsHost.Children[1]).Text.ShouldBe("b");
	}

	[Test]
	public void Observable_add_and_remove_update_containers_incrementally()
	{
		var items = new ObservableCollection<string> { "a", "b" };
		var ic = new ItemsControl { ItemsSource = items };
		ic.ItemsHost!.Children.Count.ShouldBe(2);

		items.Insert(1, "x"); // [a, x, b]
		ic.ItemsHost.Children.Count.ShouldBe(3);
		((TextBlock)ic.ItemsHost.Children[1]).Text.ShouldBe("x"); // no template ⇒ wrapped string

		items.RemoveAt(0); // [x, b]
		ic.ItemsHost.Children.Count.ShouldBe(2);
		((TextBlock)ic.ItemsHost.Children[0]).Text.ShouldBe("x");
	}

	[Test]
	public void Observable_replace_and_reset_update_containers()
	{
		var items = new ObservableCollection<string> { "a", "b", "c" };
		var ic = new ItemsControl { ItemsSource = items };

		items[1] = "B"; // Replace
		((TextBlock)ic.ItemsHost!.Children[1]).Text.ShouldBe("B");

		items.Clear(); // Reset
		ic.ItemsHost.Children.Count.ShouldBe(0);
	}

	[Test]
	public void List_view_wraps_items_in_selectable_containers()
	{
		var lv = new ListView { ItemsSource = new[] { "a", "b", "c" } };

		lv.ItemsHost!.Children.Count.ShouldBe(3);
		lv.ItemsHost.Children[0].ShouldBeOfType<ListViewItem>();
	}

	[Test]
	public void Single_selection_selects_one_and_deselects_the_previous()
	{
		var lv = new ListView { ItemsSource = new[] { "a", "b", "c" } };

		lv.SelectIndex(1);
		lv.SelectedIndex.ShouldBe(1);
		lv.SelectedItem.ShouldBe("b");
		((ListViewItem)lv.ItemsHost!.Children[1]).IsSelected.ShouldBeTrue();

		lv.SelectIndex(2);
		((ListViewItem)lv.ItemsHost.Children[1]).IsSelected.ShouldBeFalse();
		((ListViewItem)lv.ItemsHost.Children[2]).IsSelected.ShouldBeTrue();
		lv.SelectedItem.ShouldBe("c");
	}

	[Test]
	public void Pressing_a_container_selects_it()
	{
		var lv = new ListView { ItemsSource = new[] { "a", "b" } };
		var container = (ListViewItem)lv.ItemsHost!.Children[1];

		container.RaiseEvent(UIElement.PointerPressedEvent, new RoutedEventArgs());

		lv.SelectedIndex.ShouldBe(1);
		container.IsSelected.ShouldBeTrue();
	}
}
