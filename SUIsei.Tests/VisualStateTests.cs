using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// VisualStateManager coverage: GoToState stops the current state's storyboard and starts the
/// target's, groups are independent, and a detached control (no host clock) snaps to the target's end values so the
/// transition is still observable headlessly.
/// </summary>
public sealed class VisualStateTests
{
	private static Storyboard OpacityTo(UIElement element, float to) =>
		new()
		{
			Children =
			{
				new DoubleAnimation
				{
					From = 1f, To = to, Duration = TimeSpan.FromSeconds(0.2), FillBehavior = FillBehavior.HoldEnd,
					Apply = v => element.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
					Release = () => element.ClearAnimatedValue(UIElement.OpacityProperty),
				},
			},
		};

	private static VisualStateGroup CommonStates(UIElement target, out VisualState normal, out VisualState over)
	{
		normal = new VisualState { Name = "Normal" };
		over = new VisualState { Name = "PointerOver", Storyboard = OpacityTo(target, 0.5f) };
		var group = new VisualStateGroup { Name = "CommonStates" };
		group.States.Add(normal);
		group.States.Add(over);
		return group;
	}

	[Test]
	public void Go_to_state_animates_through_the_host_clock()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var control = new ContentControl { Background = new SolidColorBrush(HdrColor.FromRgb(0, 0, 0)), Width = 20, Height = 20 };
		view.Root.Content = control;
		VisualStateManager.GetVisualStateGroups(control).Add(CommonStates(control, out _, out var over));

		VisualStateManager.GoToState(control, "PointerOver", useTransitions: true).ShouldBeTrue();
		host.Tick(0.2, 0f, new Vector2(100, 100)); // run the 0.2s storyboard to completion

		control.Opacity.ShouldBe(0.5f, 0.01f);
		over.Storyboard.ShouldNotBeNull();
	}

	[Test]
	public void Detached_control_snaps_to_the_target_state_end_value()
	{
		var control = new ContentControl { Width = 20, Height = 20 }; // never attached → no clock
		VisualStateManager.GetVisualStateGroups(control).Add(CommonStates(control, out _, out _));

		VisualStateManager.GoToState(control, "PointerOver", useTransitions: true).ShouldBeTrue();

		control.Opacity.ShouldBe(0.5f, 0.001f); // snapped to the storyboard's end synchronously
	}

	[Test]
	public void Go_to_state_returns_false_for_an_unknown_state()
	{
		var control = new ContentControl();
		VisualStateManager.GetVisualStateGroups(control).Add(CommonStates(control, out _, out _));

		VisualStateManager.GoToState(control, "DoesNotExist", useTransitions: false).ShouldBeFalse();
	}

	[Test]
	public void Switching_states_updates_the_groups_current_state()
	{
		var control = new ContentControl();
		var group = CommonStates(control, out var normal, out var over);
		VisualStateManager.GetVisualStateGroups(control).Add(group);

		VisualStateManager.GoToState(control, "PointerOver", useTransitions: false);
		group.CurrentState.ShouldBe(over);
		VisualStateManager.GoToState(control, "Normal", useTransitions: false);
		group.CurrentState.ShouldBe(normal);
	}

	[Test]
	public void Groups_are_independent()
	{
		var control = new ContentControl();
		var common = CommonStates(control, out _, out var over);
		var selection = new VisualStateGroup { Name = "SelectionStates" };
		var unselected = new VisualState { Name = "Unselected" };
		var selected = new VisualState { Name = "Selected" };
		selection.States.Add(unselected);
		selection.States.Add(selected);

		var groups = VisualStateManager.GetVisualStateGroups(control);
		groups.Add(common);
		groups.Add(selection);

		VisualStateManager.GoToState(control, "PointerOver", useTransitions: false);
		VisualStateManager.GoToState(control, "Selected", useTransitions: false);

		common.CurrentState.ShouldBe(over);         // unchanged by the SelectionStates transition
		selection.CurrentState.ShouldBe(selected);
	}
}
