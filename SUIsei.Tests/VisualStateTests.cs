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

	private static readonly Vector2 Surface = new(100, 100);

	// A state whose storyboard fades the element's Opacity to `to` from wherever it is over 0.2 s, holding the end: bound
	// through Target and TargetProperty, as generated XAML binds one, or through delegates set in code.
	private static VisualState Fading(string name, UIElement element, float to, bool bound) => new()
	{
		Name = name,
		Storyboard = new Storyboard
		{
			Children =
			{
				bound
					? new DoubleAnimation { To = to, Duration = TimeSpan.FromSeconds(0.2), Target = element, TargetProperty = UIElement.OpacityProperty }
					: new DoubleAnimation
					{
						To = to,
						Duration = TimeSpan.FromSeconds(0.2),
						Apply = v => element.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
						Release = () => element.ClearAnimatedValue(UIElement.OpacityProperty),
						ReadCurrent = () => element.Opacity,
					},
			},
		},
	};

	// A control whose one group has A (Opacity to 0.3), B (no storyboard: the base look) and C (Opacity to 0.6), on a
	// host's root unless `attach` is false.
	private static (UIHost Host, ContentControl Control) ControlWithStates(bool bound = true, bool attach = true)
	{
		var host = new UIHost();
		var control = new ContentControl { Width = 20, Height = 20 };
		var group = new VisualStateGroup { Name = "Tabs" };
		group.States.Add(Fading("A", control, 0.3f, bound));
		group.States.Add(new VisualState { Name = "B" });
		group.States.Add(Fading("C", control, 0.6f, bound));
		VisualStateManager.GetVisualStateGroups(control).Add(group);
		if (attach)
			host.CreateRoot(UILayer.Default).Root.Content = control;
		return (host, control);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void Leaving_a_finished_state_for_one_with_no_storyboard_restores_the_base_value(bool bound)
	{
		var (host, control) = ControlWithStates(bound);
		VisualStateManager.GoToState(control, "A", useTransitions: true).ShouldBeTrue();
		host.Tick(0.5, 0f, Surface); // A's fade has finished, holding its end
		control.Opacity.ShouldBe(0.3f, 0.001f);
		host.Clock.HasActiveClocks.ShouldBeFalse();

		VisualStateManager.GoToState(control, "B", useTransitions: true).ShouldBeTrue();

		control.Opacity.ShouldBe(1f); // B animates nothing, and leaving A released what it held, as in WinUI
	}

	[Test]
	public void Going_to_a_state_that_animates_the_same_property_keeps_its_value()
	{
		var (host, control) = ControlWithStates();
		VisualStateManager.GoToState(control, "A", useTransitions: true);
		host.Tick(0.5, 0f, Surface);
		control.Opacity.ShouldBe(0.3f, 0.001f);

		VisualStateManager.GoToState(control, "C", useTransitions: true).ShouldBeTrue();
		// C took Opacity over from A as it began, so leaving A did not drop it to 1 before C's first frame.
		control.Opacity.ShouldBe(0.3f, 0.001f);
		host.Tick(0.1, 0.5f, Surface);
		control.Opacity.ShouldBe(0.45f, 0.001f); // halfway from A's value to C's
		host.Tick(0.5, 0.6f, Surface);
		control.Opacity.ShouldBe(0.6f, 0.001f);
		host.Clock.HasActiveClocks.ShouldBeFalse();

		VisualStateManager.GoToState(control, "B", useTransitions: true);
		control.Opacity.ShouldBe(1f); // C, finished, is released in turn
	}

	[Test]
	public void A_state_entered_without_a_transition_is_released_when_left()
	{
		var (host, control) = ControlWithStates();

		VisualStateManager.GoToState(control, "A", useTransitions: false);
		control.Opacity.ShouldBe(0.3f, 0.001f); // at once, with nothing left running on the clock
		host.Clock.HasActiveClocks.ShouldBeFalse();
		VisualStateManager.GoToState(control, "B", useTransitions: true);
		control.Opacity.ShouldBe(1f);

		VisualStateManager.GoToState(control, "A", useTransitions: false);
		VisualStateManager.GoToState(control, "C", useTransitions: false);
		control.Opacity.ShouldBe(0.6f, 0.001f);
		VisualStateManager.GoToState(control, "B", useTransitions: false);
		control.Opacity.ShouldBe(1f);
	}

	[Test]
	public void A_state_shown_with_no_clock_is_released_when_left()
	{
		var (host, control) = ControlWithStates(attach: false);

		VisualStateManager.GoToState(control, "A", useTransitions: true);
		control.Opacity.ShouldBe(0.3f, 0.001f); // snapped: there is no clock to animate it
		VisualStateManager.GoToState(control, "B", useTransitions: true);
		control.Opacity.ShouldBe(1f);

		// Shown while detached, left once on a host: the clock never held A's value, but leaving A still releases it.
		VisualStateManager.GoToState(control, "A", useTransitions: true);
		host.CreateRoot(UILayer.Default).Root.Content = control;
		VisualStateManager.GoToState(control, "B", useTransitions: true);
		control.Opacity.ShouldBe(1f);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void Going_to_the_state_a_group_is_in_does_nothing(bool useTransitions)
	{
		var (host, control) = ControlWithStates();
		VisualStateManager.GoToState(control, "A", useTransitions: true);
		host.Tick(0.1, 0f, Surface);
		control.Opacity.ShouldBe(0.65f, 0.001f); // halfway from 1 to A's 0.3

		// As WinUI's does, and as code setting every group's state whenever anything changes relies on.
		VisualStateManager.GoToState(control, "A", useTransitions).ShouldBeTrue();
		control.Opacity.ShouldBe(0.65f, 0.001f); // neither started again from 1 nor snapped to its end
		host.Tick(0.1, 0.1f, Surface);
		control.Opacity.ShouldBe(0.3f, 0.001f); // the same run, finishing on time
		host.Clock.HasActiveClocks.ShouldBeFalse();

		VisualStateManager.GoToState(control, "A", useTransitions).ShouldBeTrue();
		host.Clock.HasActiveClocks.ShouldBeFalse(); // finished, and not begun again
		control.Opacity.ShouldBe(0.3f, 0.001f);
		VisualStateManager.GoToState(control, "B", useTransitions: true);
		control.Opacity.ShouldBe(1f); // and left as any state is
	}

	/// <summary>
	/// A state whose storyboard can't begin throws before anything changes: the state being left still holds its values,
	/// nothing it animates is taken over, and the group is still in it. So going to the broken state again throws again,
	/// rather than finding it current and doing nothing.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void A_state_that_cannot_begin_changes_nothing_and_throws_again_when_retried(bool useTransitions)
	{
		var (host, control) = ControlWithStates();
		VisualStateManager.GoToState(control, "A", useTransitions: true);
		host.Tick(0.3, 0f, Surface);
		control.Opacity.ShouldBe(0.3f, 0.001f);
		var group = VisualStateManager.GetVisualStateGroups(control)[0];
		// Its opacity animation is fine; its MaxWidth one has no From, and MaxWidth is infinite by default.
		group.States.Add(new VisualState
		{
			Name = "Broken",
			Storyboard = new Storyboard
			{
				Children =
					{
						new DoubleAnimation { To = 0.9, Duration = TimeSpan.FromSeconds(0.2), Target = control, TargetProperty = UIElement.OpacityProperty },
						new DoubleAnimation { To = 50, Duration = TimeSpan.FromSeconds(0.2), Target = control, TargetProperty = FrameworkElement.MaxWidthProperty },
					},
			},
		});

		Should.Throw<InvalidOperationException>(() => VisualStateManager.GoToState(control, "Broken", useTransitions));

		group.CurrentState!.Name.ShouldBe("A");
		host.Tick(0.1, 0.3f, Surface);
		control.Opacity.ShouldBe(0.3f, 0.001f); // A still holds it, untaken
		Should.Throw<InvalidOperationException>(() => VisualStateManager.GoToState(control, "Broken", useTransitions));
		VisualStateManager.GoToState(control, "B", useTransitions: true);
		control.Opacity.ShouldBe(1f); // and A is released when left
	}

	/// <summary>
	/// A template with a group whose "Held" state fades the control's own opacity to 0.3 and holds it, and whose border
	/// part takes the control's background through a template binding.
	/// </summary>
	private static ControlTemplate HoldingTemplate(out Func<Border> part) => HoldingTemplate(out part, 0.3f);

	private static ControlTemplate HoldingTemplate(out Func<Border> part, float held)
	{
		Border? built = null;
		part = () => built!;
		return new ControlTemplate
		{
			Factory = control =>
			{
				var border = new Border();
				built = border;
				control.RegisterTemplateBinding(Control.BackgroundProperty, border, Border.BackgroundProperty);
				var group = new VisualStateGroup { Name = "Common" };
				group.States.Add(new VisualState { Name = "Normal" });
				group.States.Add(Fading("Held", control, held, bound: true));
				VisualStateManager.GetVisualStateGroups(border).Add(group);
				return border;
			},
		};
	}

	/// <summary>
	/// Replacing a template leaves its states, so one holding a property of the control itself lets go of it, and drops
	/// its template bindings, so the old parts hear nothing more from the control.
	/// </summary>
	[Test]
	public void Replacing_a_template_leaves_its_states_and_drops_its_template_bindings()
	{
		var host = new UIHost();
		var control = new ContentControl { Width = 20, Height = 20, Template = HoldingTemplate(out var oldPart) };
		host.CreateRoot(UILayer.Default).Root.Content = control;
		host.Tick(0.016, 0f, Surface);
		VisualStateManager.GoToState(control, "Held", useTransitions: true);
		host.Tick(0.3, 0.016f, Surface);
		control.Opacity.ShouldBe(0.3f, 0.001f);
		var first = oldPart();

		control.Template = HoldingTemplate(out var newPart, 0.6f);
		host.Tick(0.016, 0.32f, Surface);

		control.Opacity.ShouldBe(1f); // the old template's held state let go
		var red = new SolidColorBrush(new HdrColor(1f, 0f, 0f, 1f));
		control.Background = red;
		newPart().Background.ShouldBeSameAs(red);
		first.Background.ShouldBeNull(); // the old part is no longer pushed to
		VisualStateManager.GoToState(control, "Held", useTransitions: false).ShouldBeTrue(); // the new template's states
		control.Opacity.ShouldBe(0.6f, 0.001f);
	}

	[Test]
	public void Changing_state_and_running_frames_allocates_nothing_once_warm()
	{
		var (host, control) = ControlWithStates();
		Cycle(); // the clock's entries, and its list of storyboards holding values, are made here and reused after

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 4; i++)
				Cycle();
		});
		control.Opacity.ShouldBe(1f);

		// Fade to A and hold it, fade on to C and hold that, back to B; then the same without transitions.
		void Cycle()
		{
			VisualStateManager.GoToState(control, "A", useTransitions: true);
			Frames(20);
			VisualStateManager.GoToState(control, "C", useTransitions: true);
			Frames(20);
			VisualStateManager.GoToState(control, "B", useTransitions: true);
			Frames(2);
			VisualStateManager.GoToState(control, "A", useTransitions: false);
			Frames(2);
			VisualStateManager.GoToState(control, "B", useTransitions: false);
			Frames(2);
		}

		void Frames(int count)
		{
			for (var i = 0; i < count; i++)
			{
				host.Tick(1.0 / 60.0, 0f, Surface);
				host.Render();
			}
		}
	}
}
