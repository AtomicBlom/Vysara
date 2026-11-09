using System.Numerics;

using SUIsei.Controls;
using SUIsei.Input;

namespace SUIsei.Tests;

/// <summary>
/// The coarse per-window capture report — <see cref="UIHost.WantsPointer"/> and
/// <see cref="UIHost.WantsKeyboard"/>. Asserts the flags reflect this frame's routed state: a pointer over
/// interactive content wants the pointer (and keeps wanting it through a press-capture drag off the element),
/// empty space does not; keyboard focus on a control wants the keyboard, no focus does not.
/// </summary>
public sealed class CaptureReportTests
{
	private static readonly Vector2 Surface = new(200f, 200f);

	[Test]
	public void Pointer_over_a_button_wants_the_pointer()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new Button { Width = 100f, Height = 40f };

		// The centred button covers the surface centre; a move there lands on interactive content.
		host.QueuePointer(PointerInput.Move(new Vector2(100f, 100f)));
		host.Tick(0.016, 0f, Surface);

		host.WantsPointer.ShouldBeTrue();
	}

	[Test]
	public void Pointer_over_empty_space_does_not_want_the_pointer()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new Button { Width = 100f, Height = 40f };

		// A corner is outside the centred button — the over-chain is empty and no capture is held.
		host.QueuePointer(PointerInput.Move(new Vector2(5f, 5f)));
		host.Tick(0.016, 0f, Surface);

		host.WantsPointer.ShouldBeFalse();
	}

	[Test]
	public void Press_capture_keeps_wanting_the_pointer_during_a_drag_off_the_element()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new Button { Width = 100f, Height = 40f };

		host.QueuePointer(PointerInput.Press(new Vector2(100f, 100f)));
		host.Tick(0.016, 0f, Surface);

		// Dragged off the button, but the implicit press-capture still routes to it — so the pointer is still wanted.
		host.QueuePointer(PointerInput.Move(new Vector2(5f, 5f)));
		host.Tick(0.016, 0f, Surface);

		host.WantsPointer.ShouldBeTrue();
	}

	[Test]
	public void A_lifted_touch_leaves_what_it_was_over_and_stops_wanting_the_pointer()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var button = new Button { Width = 100f, Height = 40f };
		view.Root.Content = button;
		int exits = 0;
		button.PointerExited += (_, _) => exits++;
		var at = new Vector2(100f, 100f);

		host.QueuePointer(PointerInput.Press(at, pointerId: 5, device: PointerDeviceType.Touch));
		host.Tick(0.016, 0f, Surface);
		host.WantsPointer.ShouldBeTrue();

		// A mouse lifted would still hover; a finger lifted is gone
		host.QueuePointer(PointerInput.Release(at, pointerId: 5, device: PointerDeviceType.Touch));
		host.Tick(0.016, 0f, Surface);
		exits.ShouldBe(1);
		host.WantsPointer.ShouldBeFalse();
	}

	[Test]
	public void HitsContent_says_whether_a_press_would_land_on_the_UI()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new Button { Width = 100f, Height = 40f };
		host.Tick(0.016, 0f, Surface);

		host.HitsContent(new Vector2(100f, 100f)).ShouldBeTrue();
		host.HitsContent(new Vector2(5f, 5f)).ShouldBeFalse();
	}

	[Test]
	public void Explicit_capture_wants_the_pointer_even_over_empty_space()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var button = new Button { Width = 100f, Height = 40f };
		view.Root.Content = button;
		host.Tick(0.016, 0f, Surface);

		// Grab explicit (drag) capture, then move to an empty corner: the over-chain is empty and no press
		// happened, so only the explicit capture keeps the pointer wanted.
		button.CapturePointer(0).ShouldBeTrue();
		host.QueuePointer(PointerInput.Move(new Vector2(5f, 5f)));
		host.Tick(0.016, 0f, Surface);

		host.WantsPointer.ShouldBeTrue();
	}

	[Test]
	public void Focus_on_a_text_control_wants_the_keyboard()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var box = new TextBox { Width = 150f };
		view.Root.Content = box;
		host.Tick(0.016, 0f, Surface);

		host.Focus.TrySetFocus(box, FocusState.Programmatic);
		host.Tick(0.016, 0f, Surface);

		host.WantsKeyboard.ShouldBeTrue();
	}

	[Test]
	public void No_focus_does_not_want_the_keyboard()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = new TextBox { Width = 150f };

		host.Tick(0.016, 0f, Surface);

		host.WantsKeyboard.ShouldBeFalse();
	}
}
