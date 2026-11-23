using System.Collections.ObjectModel;
using Vysara.Controls;
using Vysara.RoutedEvents;

namespace Vysara.Tests;

/// <summary>
/// Items-control coverage: one container realized per item (through a data template or the
/// default wrap), incremental updates from an <c>ObservableCollection</c> (add/remove/replace/reset), and ListView
/// single selection driving the item's selection visual. Headless — no GPU.
/// </summary>
public sealed class ItemsControlTests
{
	// An items control follows its source while it is in a live tree; this puts it in one and returns the root
	private static UIRoot Live(UIElement element)
	{
		var root = new UIHost().CreateRoot(UILayer.Default).Root;
		root.Content = element;
		return root;
	}

	/// <summary>Out of a live tree an items control doesn't listen to its source, so a long-lived collection keeps no removed
	/// control alive; joining a tree again lays out what the source holds now.</summary>
	[Test]
	public void A_control_out_of_the_tree_lets_its_source_go_and_catches_up_as_it_returns()
	{
		var items = new ObservableCollection<string> { "a", "b" };
		var ic = new ItemsControl { ItemsSource = items };
		var root = Live(ic);

		root.Content = null;
		items.Add("c");
		ic.ItemsHost!.Children.Count.ShouldBe(2); // not heard while out

		root.Content = ic;
		ic.ItemsHost.Children.Count.ShouldBe(3);
		((TextBlock)ic.ItemsHost.Children[2]).Text.ShouldBe("c");

		items.RemoveAt(0); // heard again
		ic.ItemsHost.Children.Count.ShouldBe(2);
	}

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
		Live(ic);
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
		Live(ic);

		items[1] = "B"; // Replace
		((TextBlock)ic.ItemsHost!.Children[1]).Text.ShouldBe("B");

		items.Clear(); // Reset
		ic.ItemsHost.Children.Count.ShouldBe(0);
	}

	[Test]
	public void An_items_panel_set_after_the_items_lays_them_out_again_in_it()
	{
		// Markup sets an attribute before a property element, so a template's OneTime ItemsSource lands before its ItemsPanel
		var ic = new ItemsControl { ItemsSource = new[] { "a", "b" } };
		var first = ic.ItemsHost!;
		first.ShouldBeOfType<StackPanel>().Orientation.ShouldBe(Orientation.Vertical);

		ic.ItemsPanel = new ItemsPanelTemplate { Factory = static () => new StackPanel { Orientation = Orientation.Horizontal } };

		var panel = ic.ItemsHost.ShouldBeOfType<StackPanel>();
		panel.ShouldNotBeSameAs(first);
		panel.Orientation.ShouldBe(Orientation.Horizontal);
		panel.Parent.ShouldBe(ic);
		first.Parent.ShouldBeNull();
		panel.Children.Count.ShouldBe(2);
		((TextBlock)panel.Children[0]).Text.ShouldBe("a");
		((TextBlock)panel.Children[1]).Text.ShouldBe("b");
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

	[Test]
	public void Selection_changed_names_what_left_the_selection_and_what_joined_it()
	{
		var lv = new ListView { ItemsSource = new[] { "a", "b", "c" } };
		var changes = new List<(object? Removed, object? Added)>();
		lv.SelectionChanged += (sender, e) =>
		{
			sender.ShouldBeSameAs(lv);
			e.OriginalSource.ShouldBeSameAs(lv);
			e.Source.ShouldBeSameAs(lv);
			changes.Add((e.RemovedItems.SingleOrDefault(), e.AddedItems.SingleOrDefault()));
		};

		lv.SelectIndex(1);
		lv.SelectIndex(1); // already selected: no change
		((ListViewItem)lv.ItemsHost!.Children[2]).RaiseEvent(UIElement.PointerPressedEvent, new RoutedEventArgs());
		lv.SelectIndex(-1);
		lv.SelectIndex(-1); // nothing to clear
		lv.SelectIndex(0);
		lv.ItemsSource = new[] { "x" }; // reloading clears the selection

		changes.ShouldBe([(null, "b"), ("b", "c"), ("c", null), (null, "a"), ("a", null)]);
	}
}
