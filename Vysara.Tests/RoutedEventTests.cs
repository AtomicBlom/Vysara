using Vysara.Controls;
using Vysara.RoutedEvents;

namespace Vysara.Tests;

/// <summary>
/// Routed events: bubble target→root ordering, <c>Handled</c> short-circuit, and the
/// composite <c>Button.Click</c> synthesized from a pointer press+release (and suppressed by a press-then-leave).
/// </summary>
public sealed class RoutedEventTests
{
	[Test]
	public void Bubble_routes_target_to_root()
	{
		var evt = RoutedEvent.Register("TestBubble", RoutingStrategy.Bubble);
		var order = new List<string>();
		var root = new Border();
		var mid = new Border();
		var leaf = new Border();
		root.Child = mid;
		mid.Child = leaf;
		root.AddHandler(evt, (_, _) => order.Add("root"));
		mid.AddHandler(evt, (_, _) => order.Add("mid"));
		leaf.AddHandler(evt, (_, _) => order.Add("leaf"));

		leaf.RaiseEvent(evt, new RoutedEventArgs());

		order.ShouldBe(new[] { "leaf", "mid", "root" });
	}

	[Test]
	public void Handled_short_circuits_the_remaining_route()
	{
		var evt = RoutedEvent.Register("TestHandled", RoutingStrategy.Bubble);
		var order = new List<string>();
		var root = new Border();
		var leaf = new Border();
		root.Child = leaf;
		root.AddHandler(evt, (_, _) => order.Add("root"));
		leaf.AddHandler(evt, (_, e) => { order.Add("leaf"); e.Handled = true; });

		leaf.RaiseEvent(evt, new RoutedEventArgs());

		order.ShouldBe(new[] { "leaf" }); // root never reached
	}

	[Test]
	public void Button_click_is_synthesized_from_press_then_release()
	{
		var clicks = 0;
		var btn = new Button();
		btn.Click += (_, _) => clicks++;

		btn.RaiseEvent(UIElement.PointerPressedEvent, new PointerRoutedEventArgs());
		btn.RaiseEvent(UIElement.PointerReleasedEvent, new PointerRoutedEventArgs());

		clicks.ShouldBe(1);
	}

	[Test]
	public void Press_then_leave_does_not_click()
	{
		var clicks = 0;
		var btn = new Button();
		btn.Click += (_, _) => clicks++;

		btn.RaiseEvent(UIElement.PointerPressedEvent, new PointerRoutedEventArgs());
		btn.RaiseEvent(UIElement.PointerExitedEvent, new PointerRoutedEventArgs());
		btn.RaiseEvent(UIElement.PointerReleasedEvent, new PointerRoutedEventArgs());

		clicks.ShouldBe(0);
	}
}
