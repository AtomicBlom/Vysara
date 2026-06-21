using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="ToggleButton"/> / <see cref="CheckBox"/> (PlanningUI.md MU6): click cycles the checked state
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
}
