using System.Numerics;
using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Controls;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="ToggleButton"/> / <see cref="CheckBox"/>: click cycles the checked state
/// (two- and three-state), raises the matching routed events, the check-box template realizes and renders, and a
/// template realized under a live host snaps to the current check state rather than transitioning into it.
/// </summary>
public sealed class ToggleButtonTests
{
	[Test]
	public void Click_toggles_two_state()
	{
		var checkBox = new CheckBox();
		checkBox.IsChecked.ShouldBe(false);

		checkBox.PerformClick();
		checkBox.IsChecked.ShouldBe(true);

		checkBox.PerformClick();
		checkBox.IsChecked.ShouldBe(false);
	}

	[Test]
	public void Three_state_cycles_through_indeterminate()
	{
		var checkBox = new CheckBox { IsThreeState = true };

		checkBox.PerformClick();
		checkBox.IsChecked.ShouldBe(true);

		checkBox.PerformClick();
		checkBox.IsChecked.ShouldBeNull();

		checkBox.PerformClick();
		checkBox.IsChecked.ShouldBe(false);
	}

	[Test]
	public void Toggling_raises_checked_then_unchecked()
	{
		var checkBox = new CheckBox();
		var events = new List<string>();
		checkBox.Checked += (_, _) => events.Add("checked");
		checkBox.Unchecked += (_, _) => events.Add("unchecked");

		checkBox.PerformClick();   // -> true
		checkBox.PerformClick();   // -> false

		events.ShouldBe(new[] { "checked", "unchecked" });
	}

	[Test]
	public void CheckBox_realizes_and_renders_its_template()
	{
		var checkBox = new CheckBox { Content = "Enable" };
		var root = new UIRoot(UISurface.Hud) { Content = checkBox };

		root.LayoutPass(new Size(200f, 40f));
		root.Render().Commands.Length.ShouldBeGreaterThan(0);

		checkBox.PerformClick();   // checked: glyph shows
		root.LayoutPass(new Size(200f, 40f));
		root.Render().Commands.Length.ShouldBeGreaterThan(0);
	}

	[Test]
	public void CheckBox_box_reacts_to_the_pointer_states()
	{
		var checkBox = new CheckBox { Content = "Enable" };

		// The check-box template wires a CommonStates group on the box (hover/press feedback).
		VisualStateManager.GoToState(checkBox, "PointerOver", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(checkBox, "Pressed", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(checkBox, "Normal", useTransitions: false).ShouldBeTrue();
	}

	[Test]
	public void ToggleButton_default_template_reflects_checked_via_the_accent_overlay()
	{
		var toggle = new ToggleButton { Content = "Power" };

		// Realize the template; OnApplyTemplate snaps the initial (unchecked) state, hiding the accent overlay.
		var root = toggle.GetTemplateRoot();
		root.ShouldNotBeNull();
		var accent = root.VisualChildren[1]; // [baseBorder, accent, presenter]
		accent.Opacity.ShouldBe(0f, 0.01f);

		toggle.PerformClick();                // -> checked: accent fades fully in (snapped, no host clock)
		accent.Opacity.ShouldBe(1f, 0.01f);

		toggle.PerformClick();                // -> unchecked: accent back to hidden
		accent.Opacity.ShouldBe(0f, 0.01f);
	}

	[Test]
	public void The_initial_check_state_applies_without_a_transition()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var toggle = new ToggleButton { Content = "Power" };
		view.Root.Content = toggle;
		host.Tick(0.016, 0f, new Vector2(200f, 100f)); // the first layout realizes the template under the host's clock

		// Entering Unchecked from the template snaps; a transition would keep the clock running a no-op fade.
		host.Clock.HasActiveClocks.ShouldBeFalse();
		toggle.GetTemplateRoot()!.VisualChildren[1].Opacity.ShouldBe(0f, 0.01f); // [baseBorder, accent, presenter]
	}

	[Test]
	public void Reapplying_the_template_shows_a_checked_accent_at_once()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var toggle = new ToggleButton { Content = "Power", IsChecked = true };
		view.Root.Content = toggle;
		var size = new Vector2(200f, 100f);
		host.Tick(0.016, 0f, size);

		// Swap the template out and back in, as a style or hot reload does: the next layout realizes a fresh tree under
		// the host's clock while checked, and its accent overlay must show at once rather than fade in from hidden.
		var template = toggle.Template;
		toggle.Template = null;
		toggle.Template = template;
		host.Tick(0.016, 0.016f, size);

		host.Clock.HasActiveClocks.ShouldBeFalse();
		toggle.GetTemplateRoot()!.VisualChildren[1].Opacity.ShouldBe(1f, 0.01f); // [baseBorder, accent, presenter]
	}

	[Test]
	public void ToggleButton_registers_common_and_check_state_groups()
	{
		var toggle = new ToggleButton { Content = "Power" };

		VisualStateManager.GoToState(toggle, "PointerOver", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(toggle, "Checked", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(toggle, "Indeterminate", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(toggle, "Unchecked", useTransitions: false).ShouldBeTrue();
	}
}
