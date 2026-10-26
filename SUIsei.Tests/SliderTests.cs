using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Input;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="Slider"/>: <see cref="Slider.Value"/> is coerced into range; a track press maps
/// the pointer position to a snapped value and enters the <c>Pressed</c> visual state; dragging tracks the captured
/// pointer; the keyboard nudges the value; releasing leaves the pressed state; and the initial state applies without a
/// transition.
/// </summary>
public sealed class SliderTests
{
	private static readonly Vector2 Framebuffer = new(400f, 100f);

	// A 200×20 slider pinned at the top-left so a surface-pixel pointer position maps directly to track-local space.
	private static (UIHost Host, Slider Slider) Laid()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var slider = new Slider
		{
			Minimum = 0d,
			Maximum = 100d,
			Value = 0d,
			StepFrequency = 1d,
			Width = 200f,
			Height = 20f,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		view.Root.Content = slider;
		host.Tick(0.016, 0f, Framebuffer); // lay out + apply template (wires handlers, realizes the track)
		return (host, slider);
	}

	private static VisualStateGroup CommonStates(Slider slider) =>
		VisualStateManager.GetVisualStateGroups(slider.GetTemplateRoot()!).First(g => g.Name == "CommonStates");

	[Test]
	public void Value_is_coerced_into_the_range()
	{
		var slider = new Slider { Minimum = 0d, Maximum = 100d };

		slider.Value = 150d;
		slider.Value.ShouldBe(100d);

		slider.Value = -20d;
		slider.Value.ShouldBe(0d);
	}

	[Test]
	public void Value_changed_event_fires_on_change()
	{
		var slider = new Slider { Minimum = 0d, Maximum = 100d };
		var raised = 0;
		slider.ValueChanged += (_, _) => raised++;

		slider.Value = 42d;

		raised.ShouldBe(1);
	}

	[Test]
	public void Pressing_the_track_sets_the_value_and_enters_pressed()
	{
		var (host, slider) = Laid();

		host.QueuePointer(PointerInput.Press(new Vector2(100f, 10f))); // centre of a 200-wide track
		host.Tick(0.016, 0f, Framebuffer);

		slider.Value.ShouldBe(50d, 1.5d);                 // mid-track ≈ mid-range
		CommonStates(slider).CurrentState!.Name.ShouldBe("Pressed");
	}

	[Test]
	public void Dragging_tracks_the_captured_pointer()
	{
		var (host, slider) = Laid();

		host.QueuePointer(PointerInput.Press(new Vector2(20f, 10f)));
		host.Tick(0.016, 0f, Framebuffer);
		var low = slider.Value;

		host.QueuePointer(PointerInput.Move(new Vector2(180f, 10f), PointerButtons.Left));
		host.Tick(0.016, 0f, Framebuffer);

		slider.Value.ShouldBeGreaterThan(low);
		slider.Value.ShouldBeGreaterThan(80d); // near the right end
	}

	[Test]
	public void Releasing_leaves_the_pressed_state()
	{
		var (host, slider) = Laid();

		host.QueuePointer(PointerInput.Press(new Vector2(100f, 10f)));
		host.Tick(0.016, 0f, Framebuffer);
		host.QueuePointer(PointerInput.Release(new Vector2(100f, 10f)));
		host.Tick(0.016, 0f, Framebuffer);

		CommonStates(slider).CurrentState!.Name.ShouldNotBe("Pressed");
	}

	[Test]
	public void The_initial_state_applies_without_a_transition()
	{
		var (host, slider) = Laid();
		var thumb = (Border)((Panel)slider.GetTemplateRoot()!).Children[2];
		var normal = HdrColor.FromRgb(90, 150, 230); // the Normal thumb colour

		// Entering Normal from the template snaps to it; a transition here would fade the thumb in from the PointerOver
		// colour, a hover flash on every freshly shown slider.
		CommonStates(slider).CurrentState!.Name.ShouldBe("Normal");
		host.Clock.HasActiveClocks.ShouldBeFalse();
		thumb.Visual.Content.ShouldBeOfType<RectContent>().Fill.SolidColor().ShouldBe(normal);

		host.Tick(0.016, 0.016f, Framebuffer);
		thumb.Visual.Content.ShouldBeOfType<RectContent>().Fill.SolidColor().ShouldBe(normal);
	}

	[Test]
	public void Keyboard_nudges_and_jumps_the_value()
	{
		var (host, slider) = Laid();

		// A press focuses the slider (UIHost wires OnPress → focus), so keys route to it.
		host.QueuePointer(PointerInput.Press(new Vector2(100f, 10f)));
		host.Tick(0.016, 0f, Framebuffer);
		host.QueuePointer(PointerInput.Release(new Vector2(100f, 10f)));
		host.Tick(0.016, 0f, Framebuffer);

		host.QueueKey(new KeyInput(VirtualKey.End, true, false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Framebuffer);
		slider.Value.ShouldBe(100d);

		host.QueueKey(new KeyInput(VirtualKey.Left, true, false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Framebuffer);
		slider.Value.ShouldBe(99d); // one StepFrequency down

		host.QueueKey(new KeyInput(VirtualKey.Home, true, false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Framebuffer);
		slider.Value.ShouldBe(0d);
	}
}
