using System.Numerics;

using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="MenuFlyout"/> (PlanningUI.md MU6): <see cref="MenuFlyout.ShowAt"/> opens items in the root overlay,
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
}
