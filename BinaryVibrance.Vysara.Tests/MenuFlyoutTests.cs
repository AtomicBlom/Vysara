using System.Numerics;

using BinaryVibrance.Vysara.Controls;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="MenuFlyout"/>: <see cref="MenuFlyout.ShowAt"/> opens items in the root overlay,
/// and invoking an item fires its click and closes the menu (removing both overlays).
/// </summary>
public sealed class MenuFlyoutTests
{
	[Test]
	public void ShowAt_opens_and_item_invoke_closes_and_fires()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var anchor = new Button { Width = 80f, Height = 24f };
		view.Root.Content = anchor;
		host.Tick(0.016, 0f, new Vector2(300f, 300f));

		var invoked = 0;
		var item = new MenuFlyoutItem { Text = "Open" };
		item.Click += (_, _) => invoked++;
		var flyout = new MenuFlyout();
		flyout.Items.Add(item);

		flyout.ShowAt(anchor);
		flyout.IsOpen.ShouldBeTrue();
		view.Root.Overlays.Count.ShouldBe(2);   // light-dismiss layer + menu

		host.Tick(0.016, 0f, new Vector2(300f, 300f));

		item.PerformClick();
		invoked.ShouldBe(1);
		flyout.IsOpen.ShouldBeFalse();
		view.Root.Overlays.Count.ShouldBe(0);
	}

	[Test]
	public void ShowAt_clamps_and_scrolls_when_items_dont_fit_below_the_anchor()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var anchor = new Button { Width = 80f, Height = 24f };
		view.Root.Content = anchor;
		host.Tick(0.016, 0f, new Vector2(200f, 120f)); // a short surface: 20 items won't fit below the anchor

		var flyout = new MenuFlyout();
		for (var i = 0; i < 20; i++)
			flyout.Items.Add(new MenuFlyoutItem { Text = $"Item {i}" });

		flyout.ShowAt(anchor);
		host.Tick(0.016, 0f, new Vector2(200f, 120f));

		var menu = (Border)view.Root.Overlays[1].Element;
		var scroll = (ScrollViewer)menu.Child!;
		scroll.LayoutRect.Height.ShouldBeLessThan(scroll.ExtentHeight);
		scroll.ScrollableHeight.ShouldBeGreaterThan(0f);
	}

	[Test]
	public void ShowAt_sizes_the_menu_to_its_items_not_to_the_surface()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var anchor = new Button { Width = 80f, Height = 24f };
		view.Root.Content = anchor;
		host.Tick(0.016, 0f, new Vector2(800f, 600f));

		var flyout = new MenuFlyout();
		flyout.Items.Add(new MenuFlyoutItem { Text = "Open" });
		flyout.Items.Add(new MenuFlyoutItem { Text = "Quit" });

		flyout.ShowAt(anchor);
		host.Tick(0.016, 0f, new Vector2(800f, 600f));

		// The menu's items fit in its scroll viewer, which asks for no more than they need: the menu is only as big as
		// its items plus its padding and border, however much of the surface lies below the anchor.
		var menu = (Border)view.Root.Overlays[1].Element;
		var scroll = (ScrollViewer)menu.Child!;
		scroll.ScrollableHeight.ShouldBe(0f);
		scroll.LayoutRect.Height.ShouldBe(scroll.ExtentHeight);
		menu.LayoutRect.Height.ShouldBe(scroll.ExtentHeight + 10f);
		menu.LayoutRect.Width.ShouldBeLessThan(400f);
	}
}
