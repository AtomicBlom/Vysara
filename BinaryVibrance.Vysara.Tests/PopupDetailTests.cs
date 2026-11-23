using System.Numerics;

using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Controls.Primitives;
using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Tooltips and flyouts in detail: a tooltip hidden by the wheel, placed on each side or at the pointer, and recoloured by
/// the page; a flyout closed by a press outside or the pad's B, shown at a point, and a context menu found above the
/// element right-clicked, opened only by a right-click released where it was pressed, and never clicking the button under
/// it.
/// </summary>
public sealed class PopupDetailTests
{
	private static Button Target(out UiDriver ui, float left = 200f, float top = 150f)
	{
		var button = new Button
		{
			Content = "Target",
			Width = 80f,
			Height = 30f,
			Margin = new Thickness(left, top, 0f, 0f),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		ui = new UiDriver(button);
		return button;
	}

	private static void Rest(UiDriver ui, UIElement over)
	{
		ui.Host.QueuePointer(PointerInput.Move(UiDriver.Middle(over)));
		for (var t = 0.0; t < ToolTipService.InitialShowDelay + 0.2; t += 0.1)
			ui.Tick(0.1);
	}

	// --- Tooltips ---

	[Test]
	public void A_wheel_turn_hides_a_tooltip_until_the_pointer_moves_on()
	{
		var button = Target(out var ui);
		ToolTipService.SetToolTip(button, "Go");
		Rest(ui, button);
		ui.Host.ToolTips.Showing.ShouldNotBeNull();

		ui.Wheel(button, new Vector2(0f, -1f));
		ui.Host.ToolTips.Showing.ShouldBeNull();
		ui.Tick(1.0);
		ui.Host.ToolTips.Showing.ShouldBeNull();
	}

	[Test]
	[Arguments(PlacementMode.Left)]
	[Arguments(PlacementMode.Right)]
	[Arguments(PlacementMode.Top)]
	[Arguments(PlacementMode.Mouse)]
	public void A_tooltip_opens_on_the_side_it_asks_for(PlacementMode placement)
	{
		var button = Target(out var ui);
		ToolTipService.SetToolTip(button, "Go");
		ToolTipService.SetPlacement(button, placement);
		Rest(ui, button);

		var tip = UiDriver.Bounds(ui.Host.ToolTips.Showing!);
		var anchor = UiDriver.Bounds(button);
		var pointer = UiDriver.Middle(button);
		switch (placement)
		{
			case PlacementMode.Left: tip.Right.ShouldBeLessThanOrEqualTo(anchor.X); break;
			case PlacementMode.Right: tip.X.ShouldBeGreaterThanOrEqualTo(anchor.Right); break;
			case PlacementMode.Top: tip.Bottom.ShouldBeLessThanOrEqualTo(anchor.Y); break;
			default:
				tip.X.ShouldBe(pointer.X, 1f); // its left edge at the pointer, below it
				tip.Y.ShouldBeGreaterThan(pointer.Y);
				break;
		}
	}

	[Test]
	public void The_pages_resources_recolour_a_tooltip()
	{
		var background = new SolidColorBrush(HdrColor.FromRgb(10, 60, 30));
		var button = new Button { Content = "Go", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(100f) };
		var page = new Border { Child = button, Resources = new ResourceDictionary() };
		page.Resources.Add("ToolTipBackground", background);
		ToolTipService.SetToolTip(button, "Go");
		var ui = new UiDriver(page);
		Rest(ui, button);

		UiDriver.Parts<Border>(ui.Host.ToolTips.Showing!)[0].Background.ShouldBeSameAs(background);
	}

	// --- Flyouts ---

	[Test]
	public void A_press_outside_or_the_pads_B_closes_a_flyout()
	{
		var button = Target(out var ui);
		var flyout = new Flyout { Content = new Border { Width = 40f, Height = 20f } };

		flyout.ShowAt(button);
		ui.Tick();
		ui.Click(new Vector2(10f, 10f), new Vector2(10f, 10f));
		flyout.IsOpen.ShouldBeFalse();

		var inside = new Button { Content = "Inside" };
		flyout.Content = inside;
		flyout.ShowAt(button);
		ui.Tick();
		ui.Key(VirtualKey.GamepadB);
		flyout.IsOpen.ShouldBeFalse();
	}

	[Test]
	public void A_flyout_shown_at_a_point_opens_there()
	{
		var button = Target(out var ui);
		var content = new Border { Width = 40f, Height = 20f };
		var flyout = new Flyout { Content = content, Placement = FlyoutPlacementMode.Bottom };

		flyout.ShowAt(button, new FlyoutShowOptions { Position = new Point(10f, 5f) });
		ui.Tick();
		var anchor = UiDriver.Bounds(button);
		var shown = UiDriver.Bounds(content.Parent!);
		shown.X.ShouldBe(anchor.X + 10f, 1f);
		shown.Y.ShouldBeGreaterThan(anchor.Y + 5f);
	}

	/// <summary>A right-click opens the context menu of the nearest element at or above the one under it, and a button
	/// under it is not clicked.</summary>
	[Test]
	public void A_right_click_opens_the_nearest_context_menu_without_clicking()
	{
		var menu = new MenuFlyout();
		menu.Items.Add(new MenuFlyoutItem { Text = "Forget" });
		var button = new Button { Content = "Go" };
		var card = new Border { Padding = new Thickness(20f), Child = button, ContextFlyout = menu, Background = new SolidColorBrush(HdrColor.FromRgb(1, 1, 1)) };
		var ui = new UiDriver(UiDriver.Top(card));
		var clicks = 0;
		button.Click += (_, _) => clicks++;

		ui.Click(button, right: true);
		menu.IsOpen.ShouldBeTrue();
		menu.Target.ShouldBeSameAs(card);
		clicks.ShouldBe(0);
	}

	[Test]
	public void A_right_click_released_somewhere_else_opens_nothing()
	{
		var menu = new MenuFlyout();
		menu.Items.Add(new MenuFlyoutItem { Text = "Forget" });
		var card = new Border { Width = 100f, Height = 40f, ContextFlyout = menu, Background = new SolidColorBrush(HdrColor.FromRgb(1, 1, 1)) };
		var ui = new UiDriver(UiDriver.Top(card, new Border { Width = 100f, Height = 40f, Background = new SolidColorBrush(HdrColor.FromRgb(2, 2, 2)) }));

		ui.Click(UiDriver.Middle(card), UiDriver.Middle(card) + new Vector2(0f, 40f), right: true);
		menu.IsOpen.ShouldBeFalse();
	}
}
