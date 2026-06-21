using System.Numerics;

using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// MU3 routed event depth: tunnel (Preview) fires root→target before the bubble target→root,
/// setting <c>Handled</c> in the tunnel phase skips the bubble entirely, and a handler registered
/// with <c>handledEventsToo=true</c> still fires even when an earlier handler marked the event handled.
/// </summary>
public sealed class TunnelEventTests
{
	[Test]
	public void Tunnel_fires_root_to_target_before_the_bubble()
	{
		var order = new List<string>();

		var root = new Border();
		var mid  = new Border();
		var leaf = new Border();
		root.Child = mid;
		mid.Child  = leaf;

		var tunnel = RoutedEvent.Register("TestTunnel", RoutingStrategy.Tunnel);
		var bubble = RoutedEvent.Register("TestBubble", RoutingStrategy.Bubble);

		root.AddHandler(tunnel, (_, _) => order.Add("root-preview"));
		mid .AddHandler(tunnel, (_, _) => order.Add("mid-preview"));
		leaf.AddHandler(tunnel, (_, _) => order.Add("leaf-preview"));

		root.AddHandler(bubble, (_, _) => order.Add("root"));
		mid .AddHandler(bubble, (_, _) => order.Add("mid"));
		leaf.AddHandler(bubble, (_, _) => order.Add("leaf"));

		leaf.RaiseEvent(tunnel, new RoutedEventArgs());
		leaf.RaiseEvent(bubble, new RoutedEventArgs());

		order.ShouldBe(new[] { "root-preview", "mid-preview", "leaf-preview", "leaf", "mid", "root" });
	}

	[Test]
	public void Handled_set_in_tunnel_skips_the_bubble()
	{
		var order = new List<string>();
		var host = new UIHost();
		var view = host.CreateRoot(UIPlacement.Overlay());
		var mid  = new Border { Width = 200f, Height = 200f, Background = HdrColor.FromRgb(100, 100, 100) };
		var leaf = new Border { Width = 100f, Height = 100f, Background = HdrColor.FromRgb(200, 200, 200) };
		mid.Child           = leaf;
		view.Root.Content   = mid;

		// Mark handled in the Preview (tunnel) pass so the bubble should not fire.
		mid.AddHandler(UIElement.PreviewPointerPressedEvent, (_, e) => { order.Add("tunnel"); e.Handled = true; });
		leaf.AddHandler(UIElement.PointerPressedEvent,       (_, _) => order.Add("bubble"));

		host.QueuePointer(PointerInput.Press(new Vector2(50f, 50f)));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		order.ShouldBe(new[] { "tunnel" });
	}

	[Test]
	public void HandledEventsToo_still_fires_after_event_is_handled()
	{
		var order = new List<string>();
		var evt = RoutedEvent.Register("TestHandledToo", RoutingStrategy.Bubble);

		var root = new Border();
		var leaf = new Border();
		root.Child = leaf;

		// Normal handler on root: only runs when Handled=false.
		root.AddHandler(evt, (_, _) => order.Add("root-normal"));
		// handledEventsToo handler on root: always runs.
		root.AddHandler(evt, (_, _) => order.Add("root-always"), handledEventsToo: true);
		// leaf sets Handled=true, which should suppress root-normal but not root-always.
		leaf.AddHandler(evt, (_, e) => { order.Add("leaf"); e.Handled = true; });

		leaf.RaiseEvent(evt, new RoutedEventArgs());

		order.ShouldBe(new[] { "leaf", "root-always" });
	}

	[Test]
	public void Preview_pointer_fires_root_to_leaf_before_pressed_bubble()
	{
		var order = new List<string>();
		var host = new UIHost();
		var view = host.CreateRoot(UIPlacement.Overlay());
		var mid  = new Border { Width = 200f, Height = 200f, Background = HdrColor.FromRgb(100, 100, 100) };
		var leaf = new Border { Width = 100f, Height = 100f, Background = HdrColor.FromRgb(200, 200, 200) };
		mid.Child           = leaf;
		view.Root.Content   = mid;

		mid .AddHandler(UIElement.PreviewPointerPressedEvent, (_, _) => order.Add("mid-preview"));
		leaf.AddHandler(UIElement.PreviewPointerPressedEvent, (_, _) => order.Add("leaf-preview"));
		leaf.AddHandler(UIElement.PointerPressedEvent,        (_, _) => order.Add("leaf"));
		mid .AddHandler(UIElement.PointerPressedEvent,        (_, _) => order.Add("mid"));

		host.QueuePointer(PointerInput.Press(new Vector2(50f, 50f)));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		order.ShouldBe(new[] { "mid-preview", "leaf-preview", "leaf", "mid" });
	}
}
