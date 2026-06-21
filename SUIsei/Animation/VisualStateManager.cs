using SUIsei.Controls;

namespace SUIsei;

/// <summary>
/// Drives a control between named visual states (PlanningUI.md §D5.7). The <see cref="VisualStateGroupsProperty"/>
/// attached property carries the groups on a control's template root; <see cref="GoToState"/> stops the current
/// state's storyboard and starts the target's. When the control is attached to a host its
/// <see cref="AnimationClock"/> animates the transition; when detached (or <paramref name="useTransitions"/> is
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

		foreach (var group in groups)
		{
			VisualState? target = null;
			foreach (var state in group.States)
				if (state.Name == stateName)
				{
					target = state;
					break;
				}
			if (target is null)
				continue;

			var clock = control.Clock;
			if (group.CurrentState?.Storyboard is { } previous)
				clock?.Stop(previous);

			group.CurrentState = target;

			if (target.Storyboard is { } storyboard)
			{
				if (useTransitions && clock is not null)
					clock.Begin(storyboard);
				else
					SnapToEnd(storyboard);
			}
			return true;
		}

		return false;
	}

	// Apply each animation's end value immediately (no clock) — used when transitions are off or the control is
	// detached from a host, so a state change is still observable in headless tests.
	private static void SnapToEnd(Storyboard storyboard)
	{
		foreach (var child in storyboard.Children)
			if (child is AnimationTimeline anim)
				anim.ApplyAt(1.0);
	}
}
