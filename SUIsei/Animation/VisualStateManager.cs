using SUIsei.Controls;

namespace SUIsei.Animation;

/// <summary>
/// Drives a control between named visual states. The <see cref="VisualStateGroupsProperty"/>
/// attached property carries the groups on a control's template root; <see cref="GoToState"/> stops the current
/// state's storyboard and starts the target's. When the control is attached to a host its
/// <see cref="AnimationClock"/> animates the transition; when detached (or <c>useTransitions</c> is
/// false) the target state's end values are applied synchronously so headless tests still observe the result.
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
	/// itself when it has no template).
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

			var clock = control.Clock;
			var previous = group.CurrentState?.Storyboard;
			group.CurrentState = target;
			var next = target.Storyboard;

			if (next is not null && useTransitions && clock is not null)
			{
				// Begin first: an animation with no From starts at the value the previous state's animation is showing, so
				// the transition hands over smoothly. Then release the previous state.
				clock.Begin(next);
				if (previous is not null && !ReferenceEquals(previous, next))
					clock.Stop(previous);
			}
			else
			{
				// Release the previous state first, so its release cannot wipe the end values snapped next.
				if (previous is not null)
					clock?.Stop(previous);
				if (next is not null)
					SnapToEnd(next);
			}
			return true;
		}

		return false;
	}

	// Apply each animation's end value immediately (no clock) — used when transitions are off or the control is
	// detached from a host, so a state change is still observable in headless tests.
	private static void SnapToEnd(Storyboard storyboard)
	{
		var children = storyboard.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline anim)
			{
				anim.EnsureResolved();
				anim.CaptureStart();
				anim.ApplyAt(1.0);
			}
		}
	}
}
