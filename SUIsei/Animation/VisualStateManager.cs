using SUIsei.Controls;

namespace SUIsei.Animation;

/// <summary>
/// Drives a control between named visual states. The <see cref="VisualStateGroupsProperty"/>
/// attached property carries the groups on a control's template root; <see cref="GoToState"/> stops the current
/// state's storyboard and starts the target's. When the control is attached to a host its
/// <see cref="AnimationClock"/> animates the transition; when detached (or <c>useTransitions</c> is
/// false) the target state's end values are applied synchronously so headless tests still observe the result. As in
/// WinUI, leaving a state releases what it set, whether its storyboard is still running or has finished, except a
/// property the state entered animates too.
/// </summary>
public static class VisualStateManager
{
	/// <summary>The visual-state groups attached to a control's template root.</summary>
	public static readonly DependencyProperty<FrameworkElement, List<VisualStateGroup>?> VisualStateGroupsProperty =
		DependencyProperty.RegisterAttached("VisualStateGroups",
			new PropertyMetadata<FrameworkElement, List<VisualStateGroup>?> { DefaultValue = null });

	/// <summary>Get (lazily creating) the visual-state group list attached to <paramref name="host"/>.</summary>
	public static List<VisualStateGroup> GetVisualStateGroups(FrameworkElement host)
	{
		var groups = host.GetValue(VisualStateGroupsProperty);
		if (groups is null)
		{
			groups = [];
			host.SetValue(VisualStateGroupsProperty, groups);
		}
		return groups;
	}

	/// <summary>Attach <paramref name="groups"/> to <paramref name="host"/> (called by a generated template factory).</summary>
	public static void SetVisualStateGroups(FrameworkElement host, List<VisualStateGroup> groups) =>
		host.SetValue(VisualStateGroupsProperty, groups);

	/// <summary>
	/// Transition <paramref name="control"/> to the state named <paramref name="stateName"/>. Returns false when no
	/// group contains that state. The groups are read from the control's template root (falling back to the control
	/// itself when it has no template). Leaving a state releases every property its storyboard holds, whether that is
	/// still running or has finished and holds its end values, except one the state entered has taken over. As in WinUI,
	/// going to the state a group is already in does nothing and returns true: a storyboard still running carries on rather
	/// than starting again, so code may set every group's state whenever anything changes.
	/// </summary>
	public static bool GoToState(Control control, string stateName, bool useTransitions)
	{
		var host = control.GetTemplateRoot() ?? control;
		var groups = host.GetValue(VisualStateGroupsProperty);
		if (groups is null)
			return false;

		// Indexed loops: a state change runs on every hover, and a foreach over the IList would box an enumerator.
		foreach (var group in groups)
		{
			VisualState? target = null;
			var states = group.States;
			for (var i = 0; i < states.Count; i++)
			{
				if (states[i].Name == stateName)
				{
					target = states[i];
					break;
				}
			}
			if (target is null)
				continue;
			if (ReferenceEquals(group.CurrentState, target))
				return true; // already in it: its storyboard carries on, or keeps holding, untouched

			var clock = control.Clock;
			var previous = group.CurrentState?.Storyboard;
			var previousClock = group.CurrentClock;
			var next = target.Storyboard;

			if (next is not null && useTransitions && clock is not null)
			{
				// Begin first: an animation with no From starts at the value the previous state's animation is showing, so
				// the transition hands over smoothly, and takes over each property both states animate. Then release the
				// previous state, which leaves those to it.
				clock.Begin(next);
				if (previous is not null && !ReferenceEquals(previous, next))
					Leave(previous, previousClock, next);
			}
			else
			{
				// Release the previous state first, so its release cannot wipe the end values shown next. On a clock they are
				// held as a finished storyboard's are, so leaving this state releases them in turn. The next state is checked
				// before anything is released, so one that can't begin leaves the previous state showing.
				if (next is not null)
					Check(next);
				if (previous is not null && !ReferenceEquals(previous, next))
					Leave(previous, previousClock, null);
				if (next is not null && clock is not null)
					clock.SkipToFill(next);
				else if (next is not null)
					SnapToEnd(next);
			}
			// Only once the state's storyboard has begun: one that can't begin throws before anything changes, and leaves
			// the group in the state it was in, so going to the state again tries again rather than finding it current.
			group.CurrentState = target;
			group.CurrentClock = next is null ? null : clock;
			return true;
		}

		return false;
	}

	// Release what the state being left set, as leaving a WinUI visual state does: through the clock its storyboard ran or
	// held its values on, which leaves alone any property a later animation has taken over, or, for a state shown with no
	// clock, directly, except the properties the state being entered (`next`) animates now.
	private static void Leave(Storyboard previous, AnimationClock? clock, Storyboard? next)
	{
		if (clock is not null)
		{
			clock.Stop(previous);
			return;
		}
		var children = previous.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline anim && (next is null || !Animates(next, anim)))
				anim.ReleaseTarget();
		}
	}

	// Whether one of the storyboard's animations drives the property `anim` drives.
	private static bool Animates(Storyboard storyboard, AnimationTimeline anim)
	{
		var children = storyboard.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline other && other.DrivesSamePropertyAs(anim))
				return true;
		}
		return false;
	}

	// Apply each animation's end value immediately — used when the control is detached from a host and has no clock, so
	// a state change is still observable in headless tests. Leaving the state releases the values directly.
	private static void SnapToEnd(Storyboard storyboard)
	{
		Check(storyboard);
		var children = storyboard.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline anim)
			{
				anim.CaptureStart();
				anim.ApplyAt(1.0);
			}
		}
	}

	// Throw, changing nothing, if any animation of the storyboard can't begin (AnimationTimeline.CheckStart).
	private static void Check(Storyboard storyboard)
	{
		var children = storyboard.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline anim)
				anim.CheckStart();
		}
	}
}
