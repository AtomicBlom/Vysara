using System.Numerics;
using Vysara.Controls;
using Vysara.Input;
using Vysara.Media;

namespace Vysara.Tests;

/// <summary>
/// Host end-to-end: queue neutral pointer events, <see cref="UIHost.Tick"/> headless (no engine/backend), and assert
/// the routed result — a click fires its handler, <see cref="UIHost.DumpState"/> reflects the over-chain,
/// <see cref="UIHost.Render"/> yields one draw list per view, and two hosts are independent.
/// </summary>
public sealed class UIHostTests
{
	[Test]
	public void Queued_press_release_over_a_button_fires_click()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var clicks = 0;
		var btn = new Button { Width = 100f, Height = 40f };
		btn.Click += (_, _) => clicks++;
		view.Root.Content = btn;

		// The button has an explicit size and default (Stretch) alignment, so it is centred in the 200x200 surface.
		host.QueuePointer(PointerInput.Press(new Vector2(100f, 100f)));
		host.QueuePointer(PointerInput.Release(new Vector2(100f, 100f)));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		clicks.ShouldBe(1);
	}

	[Test]
	public void DumpState_reports_the_over_chain()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new Button { Width = 100f, Height = 40f };

		host.QueuePointer(PointerInput.Move(new Vector2(100f, 100f))); // over the centred button
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		host.DumpState().ShouldContain("Button");
	}

	[Test]
	public void Render_produces_one_surface_per_view_keyed_to_the_hud()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) };

		host.Tick(0.016, 0f, new Vector2(100f, 100f));
		host.Render();

		host.DrawLists.Count.ShouldBe(1);
		host.DrawLists[0].Id.ShouldBe(UISurface.Hud);
		host.DrawLists[0].Data.Commands.Length.ShouldBeGreaterThan(0);
	}

	[Test]
	public void Two_hosts_route_input_independently()
	{
		var h1 = new UIHost();
		var h2 = new UIHost();
		var c1 = 0;
		var c2 = 0;
		var b1 = new Button { Width = 100f, Height = 40f };
		var b2 = new Button { Width = 100f, Height = 40f };
		b1.Click += (_, _) => c1++;
		b2.Click += (_, _) => c2++;
		h1.CreateRoot(UILayer.Default).Root.Content = b1;
		h2.CreateRoot(UILayer.Default).Root.Content = b2;

		// Each button is centred in its 200x200 surface (explicit size + default Stretch); click h1's centre.
		h1.QueuePointer(PointerInput.Press(new Vector2(100f, 100f)));
		h1.QueuePointer(PointerInput.Release(new Vector2(100f, 100f)));
		h1.Tick(0.016, 0f, new Vector2(200f, 200f));
		h2.Tick(0.016, 0f, new Vector2(200f, 200f)); // no input queued on h2

		c1.ShouldBe(1);
		c2.ShouldBe(0);
	}
}
