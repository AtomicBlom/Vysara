using System.Numerics;

using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Controls.Primitives;
using BinaryVibrance.Vysara.Input;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="ListView"/> in detail, as WinUI's: a scroll viewer of its own, the keyboard moving up and by pages, an item
/// invoked by Space or the pad's A or by a press and release on it, keys a control in an item handles staying with it, a
/// selected item set from code ignored where it isn't in the list, an item's <see cref="ListViewItem.IsSelected"/>
/// selecting it, and the presenter showing the hover and press.
/// </summary>
public sealed class ListViewDetailTests
{
	private static (UiDriver Ui, ListView List) Hosted(int count, float height = 0f)
	{
		var list = new ListView { ItemsSource = Enumerable.Range(0, count).Select(i => $"item {i}").ToArray(), VerticalAlignment = VerticalAlignment.Top };
		if (height > 0f)
			list.Height = height;
		return (new UiDriver(list), list);
	}

	private static ListViewItem Item(ListView list, int index) => (ListViewItem)list.ItemsHost!.Children[index];

	[Test]
	public void A_list_scrolls_itself_and_brings_the_focused_item_into_view()
	{
		var (ui, list) = Hosted(40, height: 120f);
		var viewer = list.ItemsScrollViewer.ShouldNotBeNull();
		viewer.Content.ShouldBeSameAs(list.ItemsHost);

		Item(list, 0).Focus(FocusState.Keyboard);
		ui.Key(VirtualKey.End);
		list.SelectedIndex.ShouldBe(39);
		ui.Tick();
		viewer.VerticalOffset.ShouldBeGreaterThan(0f);
		UiDriver.Bounds(Item(list, 39)).Bottom.ShouldBeLessThanOrEqualTo(UiDriver.Bounds(list).Bottom + 0.5f);
	}

	[Test]
	public void The_keyboard_moves_up_and_by_pages()
	{
		var (ui, list) = Hosted(30);
		list.SelectedIndex = 15;
		Item(list, 15).Focus(FocusState.Keyboard);

		ui.Key(VirtualKey.Up);
		list.SelectedIndex.ShouldBe(14);
		ui.Key(VirtualKey.PageDown);
		list.SelectedIndex.ShouldBe(24);
		ui.Key(VirtualKey.PageUp);
		list.SelectedIndex.ShouldBe(14);
		ui.Key(VirtualKey.GamepadDPadDown);
		list.SelectedIndex.ShouldBe(15);
	}

	[Test]
	[Arguments(VirtualKey.Space)]
	[Arguments(VirtualKey.GamepadA)]
	public void Space_and_the_pads_A_invoke_an_item(VirtualKey key)
	{
		var (ui, list) = Hosted(3);
		list.IsItemClickEnabled = true;
		object? clicked = null;
		list.ItemClick += (_, e) => clicked = e.ClickedItem;

		Item(list, 2).Focus(FocusState.Keyboard);
		ui.Key(key);
		clicked.ShouldBe("item 2");
	}

	/// <summary>A click is a press and a release on the same item: one dragged from another item to it clicks neither.</summary>
	[Test]
	public void An_item_is_clicked_only_by_a_press_and_release_on_it()
	{
		var (ui, list) = Hosted(3);
		list.IsItemClickEnabled = true;
		var clicks = new List<object?>();
		list.ItemClick += (_, e) => clicks.Add(e.ClickedItem);

		ui.Click(UiDriver.Middle(Item(list, 0)), UiDriver.Middle(Item(list, 2)));
		clicks.ShouldBeEmpty();
		ui.Click(Item(list, 1));
		clicks.ShouldBe(["item 1"]);
		ui.Click(Item(list, 2), right: true);
		clicks.Count.ShouldBe(1);
		list.SelectedIndex.ShouldBe(1); // a right-click doesn't select either
	}

	/// <summary>Keys a control in an item handles are its own: Home and End in a text box move its caret, not the list.</summary>
	[Test]
	public void Keys_a_text_box_in_an_item_handles_stay_with_it()
	{
		var list = new ListView
		{
			ItemsSource = new[] { "a", "b", "c" },
			ItemTemplate = new DataTemplate { Factory = item => new TextBox { Text = (string)item! } },
			VerticalAlignment = VerticalAlignment.Top,
		};
		var ui = new UiDriver(list);
		list.SelectedIndex = 1;
		UiDriver.Parts<TextBox>(Item(list, 1))[0].Focus(FocusState.Keyboard);

		ui.Key(VirtualKey.End);
		ui.Key(VirtualKey.Home);
		list.SelectedIndex.ShouldBe(1);
	}

	[Test]
	public void A_selected_item_not_in_the_list_is_ignored_once_it_has_items()
	{
		var (ui, list) = Hosted(3);
		list.SelectedIndex = 1;
		var changes = 0;
		list.SelectedItemChanged += (_, _) => changes++;

		list.SelectedItem = "not there";
		list.SelectedItem.ShouldBe("item 1");
		list.SelectedIndex.ShouldBe(1);
		Item(list, 1).IsSelected.ShouldBeTrue();
		ui.Tick();
	}

	[Test]
	public void An_index_with_no_item_clears_the_selection_raising_one_change()
	{
		var (_, list) = Hosted(3);
		list.SelectedIndex = 1;
		var changes = 0;
		list.SelectedIndexChanged += (_, _) => changes++;

		list.SelectedIndex = 9;
		list.SelectedIndex.ShouldBe(-1);
		changes.ShouldBe(1);
	}

	[Test]
	public void Setting_an_items_IsSelected_selects_it_in_its_list()
	{
		var (_, list) = Hosted(3);
		var changes = 0;
		list.SelectionChanged += (_, _) => changes++;

		Item(list, 2).IsSelected = true;
		list.SelectedIndex.ShouldBe(2);
		Item(list, 2).IsSelected = false;
		list.SelectedIndex.ShouldBe(-1);
		changes.ShouldBe(2);
	}

	[Test]
	public void The_presenter_shows_hover_and_press()
	{
		var (ui, list) = Hosted(2);
		var item = Item(list, 0);
		var presenter = item.GetTemplateRoot().ShouldBeOfType<ListViewItemPresenter>();
		var rest = presenter.Background;

		ui.Host.QueuePointer(PointerInput.Move(UiDriver.Middle(item)));
		ui.Tick();
		presenter.Background.ShouldBeSameAs(presenter.PointerOverBackground);
		ui.Host.QueuePointer(PointerInput.Press(UiDriver.Middle(item)));
		ui.Tick();
		presenter.Background.ShouldBeSameAs(presenter.SelectedPressedBackground ?? presenter.SelectedPointerOverBackground); // pressing selects it
		ui.Host.QueuePointer(PointerInput.Release(UiDriver.Middle(item)));
		ui.Host.QueuePointer(PointerInput.Move(new Vector2(450f, 380f)));
		ui.Tick();
		presenter.Background.ShouldBeSameAs(presenter.SelectedBackground);
		rest.ShouldNotBeSameAs(presenter.SelectedBackground);
	}
}
