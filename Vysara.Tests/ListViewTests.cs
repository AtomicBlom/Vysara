using System.Numerics;

using Vysara.Controls;
using Vysara.Input;
using Vysara.Media;

namespace Vysara.Tests;

/// <summary>
/// <see cref="ListView"/> as a list: its selection set and read through dependency properties with change events, moved
/// by the keyboard past items whose content is collapsed, invoked with <see cref="ListView.ItemClick"/>; its items laid
/// out inside their padding, closing up when their content collapses, and recoloured by the page's resources.
/// </summary>
public sealed class ListViewTests
{
	private static readonly Vector2 Surface = new(300f, 400f);

	private static (UIHost Host, ListView List) Hosted(params string[] items)
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var list = new ListView { ItemsSource = items, VerticalAlignment = VerticalAlignment.Top };
		view.Root.Content = list;
		host.Tick(0.016, 0f, Surface);
		return (host, list);
	}

	private static ListViewItem Item(ListView list, int index) => (ListViewItem)list.ItemsHost!.Children[index];

	private static void Key(UIHost host, VirtualKey key)
	{
		host.QueueKey(new KeyInput(key, Down: true, Repeat: false, ModifierKeys.None, 0));
		host.QueueKey(new KeyInput(key, Down: false, Repeat: false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Surface);
	}

	[Test]
	public void Setting_the_selection_properties_selects_and_raises_their_events()
	{
		var (_, list) = Hosted("a", "b", "c");
		int indexChanges = 0, itemChanges = 0;
		list.SelectedIndexChanged += (_, _) => indexChanges++;
		list.SelectedItemChanged += (_, _) => itemChanges++;

		list.SelectedIndex = 1;
		list.SelectedItem.ShouldBe("b");
		Item(list, 1).IsSelected.ShouldBeTrue();

		list.SelectedItem = "c";
		list.SelectedIndex.ShouldBe(2);
		Item(list, 1).IsSelected.ShouldBeFalse();

		(indexChanges, itemChanges).ShouldBe((2, 2));
	}

	[Test]
	public void A_selection_set_before_its_item_arrives_is_made_as_it_does()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var list = new ListView { SelectedItem = "b" };
		view.Root.Content = list;
		list.ItemsSource = new[] { "a", "b" };
		host.Tick(0.016, 0f, Surface);

		list.SelectedIndex.ShouldBe(1);
	}

	[Test]
	public void The_keyboard_moves_the_selection_past_collapsed_items()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var hidden = new TextBlock { Text = "hidden", Visibility = Visibility.Collapsed };
		var list = new ListView { ItemsSource = new object[] { new TextBlock { Text = "a" }, hidden, new TextBlock { Text = "c" } } };
		view.Root.Content = list;
		host.Tick(0.016, 0f, Surface);

		list.SelectIndex(0);
		Item(list, 0).Focus(FocusState.Keyboard);
		Key(host, VirtualKey.Down);
		list.SelectedIndex.ShouldBe(2);
		host.Focus.FocusedElement.ShouldBe(Item(list, 2));

		Key(host, VirtualKey.Home);
		list.SelectedIndex.ShouldBe(0);
		Key(host, VirtualKey.End);
		list.SelectedIndex.ShouldBe(2);
	}

	[Test]
	public void Enter_and_a_click_invoke_an_item()
	{
		var (host, list) = Hosted("a", "b");
		list.IsItemClickEnabled = true;
		var clicked = new List<object?>();
		list.ItemClick += (_, e) => clicked.Add(e.ClickedItem);

		Item(list, 1).Focus(FocusState.Keyboard);
		Key(host, VirtualKey.Enter);

		var first = Item(list, 0).LayoutRect;
		var point = new Vector2(first.X + 5f, first.Y + first.Height / 2f);
		host.QueuePointer(PointerInput.Press(point));
		host.QueuePointer(PointerInput.Release(point));
		host.Tick(0.016, 0f, Surface);

		clicked.ShouldBe(["b", "a"]);
		list.SelectedIndex.ShouldBe(0);
	}

	[Test]
	public void An_item_lays_its_content_out_inside_its_padding_and_closes_up_when_it_collapses()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var text = new TextBlock { Text = "row" };
		var list = new ListView { ItemsSource = new object[] { text }, VerticalAlignment = VerticalAlignment.Top };
		view.Root.Content = list;
		host.Tick(0.016, 0f, Surface);

		var item = Item(list, 0);
		text.LayoutRect.X.ShouldBe(item.Padding.Left);
		item.LayoutRect.Height.ShouldBe(text.LayoutRect.Height + item.Padding.Vertical, 0.01f);

		text.Visibility = Visibility.Collapsed;
		host.Tick(0.016, 0f, Surface);
		item.LayoutRect.Height.ShouldBe(0f);
	}

	[Test]
	public void The_pages_resources_recolour_the_selection()
	{
		var accent = new SolidColorBrush(HdrColor.FromRgb(92, 170, 255));
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var list = new ListView { ItemsSource = new[] { "a" } };
		var page = new Border { Child = list, Resources = new ResourceDictionary() };
		page.Resources.Add("ListViewItemBackgroundSelected", accent);
		view.Root.Content = page;
		host.Tick(0.016, 0f, Surface);

		list.SelectIndex(0);
		host.Tick(0.2, 0f, Surface);
		var presenter = Item(list, 0).GetTemplateRoot().ShouldBeOfType<Vysara.Controls.Primitives.ListViewItemPresenter>();
		presenter.SelectedBackground.ShouldBeSameAs(accent);
		presenter.Background.ShouldBeSameAs(accent);
		presenter.SelectedPointerOverBackground.ShouldBeSameAs(accent); // hovered, the page's selection colour still
	}
}
