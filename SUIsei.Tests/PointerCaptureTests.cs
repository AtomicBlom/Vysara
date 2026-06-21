using System.Numerics;

using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// MU3 explicit pointer capture: <see cref="UIElement.CapturePointer"/> routes move/release events to the
/// captee regardless of where the pointer actually is; <see cref="UIElement.ReleasePointerCapture"/> raises
/// <see cref="UIElement.PointerCaptureLostEvent"/> and returns routing to the hit element.
/// </summary>
public sealed class PointerCaptureTests
{
	private static UIHost MakeHost(out UIView view)
	{
		var host = new UIHost();
		view = host.CreateRoot(UIPlacement.Overlay());
		return host;
	}

	[Test]
	public void CapturePointer_routes_move_outside_the_element_to_the_captee()
	{
		var host = MakeHost(out var view);
		// Two buttons side by side in a 200×100 surface.
		var left  = new Button { Width = 80f, Height = 40f };
		var right = new Button { Width = 80f, Height = 40f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(left);
		panel.Children.Add(right);
		view.Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		// Press the left button and grab capture.
		var movesOnLeft  = 0;
		var movesOnRight = 0;
		left .AddHandler(UIElement.PointerMovedEvent, (_, _) => movesOnLeft++);
		right.AddHandler(UIElement.PointerMovedEvent, (_, _) => movesOnRight++);

		host.QueuePointer(PointerInput.Press(new Vector2(40f, 50f)));
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		left.CapturePointer(0).ShouldBeTrue();

		// Move the pointer to the right button's territory — left should still receive it.
		host.QueuePointer(PointerInput.Move(new Vector2(160f, 50f)));
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		movesOnLeft.ShouldBe(1);
		movesOnRight.ShouldBe(0);
	}

	[Test]
	public void ReleasePointerCapture_raises_PointerCaptureLost_on_the_captee()
	{
		var host = MakeHost(out var view);
		var btn = new Button { Width = 100f, Height = 40f };
		view.Root.Content = btn;
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		var captureLostFired = 0;
		btn.PointerCaptureLost += (_, _) => captureLostFired++;

		// Press to get into the host, then grab explicit capture and release it.
		host.QueuePointer(PointerInput.Press(new Vector2(100f, 100f)));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		btn.CapturePointer(0);
		btn.ReleasePointerCapture(0);

		captureLostFired.ShouldBe(1);
	}

	[Test]
	public void New_explicit_capture_raises_PointerCaptureLost_on_the_previous_captee()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 80f, Height = 40f };
		var b = new Button { Width = 80f, Height = 40f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		view.Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		host.QueuePointer(PointerInput.Press(new Vector2(40f, 50f)));
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		var aLost = 0;
		a.PointerCaptureLost += (_, _) => aLost++;

		a.CapturePointer(0);
		b.CapturePointer(0); // stealing capture from a

		aLost.ShouldBe(1);
		host.Input.GetCapture(0).ShouldBe(b);
	}

	[Test]
	public void Release_by_non_captee_is_a_no_op()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 80f, Height = 40f };
		var b = new Button { Width = 80f, Height = 40f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		view.Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		host.QueuePointer(PointerInput.Press(new Vector2(40f, 50f)));
		host.Tick(0.016, 0f, new Vector2(200f, 100f));
		a.CapturePointer(0);

		var aLost = 0;
		a.PointerCaptureLost += (_, _) => aLost++;

		b.ReleasePointerCapture(0); // b is not the captee — should do nothing

		aLost.ShouldBe(0);
		host.Input.GetCapture(0).ShouldBe(a);
	}
}
