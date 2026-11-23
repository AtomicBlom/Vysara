using Vysara.Controls;

namespace Vysara.Animation;

/// <summary>
/// Drives a control between named visual states. The <see cref="VisualStateGroupsProperty"/>
/// attached property carries the groups on a control's template root; <see cref="GoToState"/> starts the target state's
/// storyboard, then stops the current state's. When the control is attached to a host its
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
	/// still running or has finished and holds its end values, except one the state entered has taken over. The state
	/// entered begins first, so an animation of it with no From starts from the value the state left shows, and a
	/// storyboard that can't begin throws with the group still in, and still showing, the state it was in. As in WinUI,
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

			// Begin the state entered, then release the state left, with a transition or without, as WPF's "start new, then
			// stop old" does: an animation with no From starts at the value the state being left shows, so a transition hands
			// over smoothly, and one that can't begin throws while the state being left still shows. On the same clock the
			// state entered takes over each property both animate; elsewhere leaving the state keeps those for it. Without a
			// transition the state entered shows its end values at once and holds them.
			if (next is not null)
			{
				if (clock is null)
					SnapToEnd(next);
				else if (useTransitions)
					clock.Begin(next);
				else
					clock.SkipToFill(next);
			}
			if (previous is not null && !ReferenceEquals(previous, next))
				Leave(previous, previousClock, next, clock);
			// Only once the state's storyboard has begun: one that can't begin throws before anything changes, and leaves
			// the group in the state it was in, so going to the state again tries again rather than finding it current.
			group.CurrentState = target;
			group.CurrentClock = next is null ? null : clock;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Leave every group's current state on <paramref name="host"/>, releasing what each holds through the clock it ran on,
	/// as a control does when its template is replaced: a state of the old template may hold a property of the control itself,
	/// or of an object reached through a path that outlives the template, such as a shared brush. The host is the old
	/// template's root, or the control itself when it showed the states of its own groups with no template realized.
	/// </summary>
	internal static void LeaveAll(UIElement host)
	{
		var groups = host.GetValue(VisualStateGroupsProperty);
		if (groups is null)
			return;
		foreach (var group in groups)
		{
			if (group.CurrentState?.Storyboard is { } storyboard)
				Leave(storyboard, group.CurrentClock, null, null);
			group.CurrentState = null;
			group.CurrentClock = null;
		}
	}

	// Release what the state being left set, as leaving a WinUI visual state does, except the properties the state being
	// entered (`next`) animates now: through the clock its storyboard ran or held its values on, or, for a state shown with
	// no clock, directly. On the clock `next` began on (`nextClock`), `next` has taken those properties over, so the clock
	// leaves them to it already, along with any another storyboard has taken.
	private static void Leave(Storyboard previous, AnimationClock? clock, Storyboard? next, AnimationClock? nextClock)
	{
		if (clock is not null)
		{
			clock.StopExcept(previous, ReferenceEquals(clock, nextClock) ? null : next);
			return;
		}
		var children = previous.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline anim && (next is null || !AnimationClock.Animates(next, anim)))
				anim.ReleaseTarget();
		}
	}

	// Apply each animation's end value immediately — used when the control is detached from a host and has no clock, so
	// a state change is still observable in headless tests. Every animation is checked first, as one, so a storyboard that
	// can't begin changes nothing. Leaving the state releases the values directly.
	private static void SnapToEnd(Storyboard storyboard)
	{
		AnimationClock.CheckStart(storyboard);
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
}
