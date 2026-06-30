using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="ToggleButton"/> / <see cref="CheckBox"/>: click cycles the checked state
/// (two- and three-state), raises the matching routed events, and the check-box template realizes and renders.
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
	public void ToggleButton_registers_common_and_check_state_groups()
	{
		var toggle = new ToggleButton { Content = "Power" };

		VisualStateManager.GoToState(toggle, "PointerOver", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(toggle, "Checked", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(toggle, "Indeterminate", useTransitions: false).ShouldBeTrue();
		VisualStateManager.GoToState(toggle, "Unchecked", useTransitions: false).ShouldBeTrue();
	}
}
