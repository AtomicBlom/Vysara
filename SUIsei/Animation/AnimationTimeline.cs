namespace SUIsei;

/// <summary>
/// A <see cref="Timeline"/> that drives one target property (PlanningUI.md §D5.7). The <see cref="AnimationClock"/>
/// only needs to push the current value and to release the target on stop, so the non-generic base exposes just
/// those two seams; the typed value + apply/release closures live on <see cref="AnimationTimeline{T}"/>. The closures
/// are resolved (by the generator, or by hand) to a typed write — no reflection.
/// </summary>
public abstract class AnimationTimeline : Timeline
{
	/// <summary>Compute the value at normalized <paramref name="progress"/> (in <c>[0,1]</c>) and push it to the target.</summary>
	internal abstract void ApplyAt(double progress);

	/// <summary>Release the target (clear the animated value) — used on <see cref="FillBehavior.Stop"/> and on stop.</summary>
	internal abstract void ReleaseTarget();

	/// <summary>Resolve the declarative <c>Target</c>/<c>TargetProperty</c> (as set by generated XAML) into the typed
	/// apply/release, once, before the storyboard runs. A no-op when an explicit apply closure was set in code.</summary>
	internal abstract void EnsureResolved();
}

/// <summary>
/// The typed animation: produces a <typeparamref name="T"/> at a given progress (<see cref="ValueAt"/>) and writes
/// it through <see cref="Apply"/>. <see cref="Release"/> reverts the target. Concrete subclasses (Double/Color/Point)
/// implement <see cref="ValueAt"/> and apply their own easing inside it.
/// </summary>
public abstract class AnimationTimeline<T> : AnimationTimeline
{
	/// <summary>The typed write that pushes a value to the target (e.g. <c>v =&gt; el.SetAnimatedValue(OpacityProperty, (float)v)</c>).</summary>
	public Action<T>? Apply { get; set; }

	/// <summary>Reverts the target to its underlying value (e.g. <c>() =&gt; el.ClearAnimatedValue(OpacityProperty)</c>).</summary>
	public Action? Release { get; set; }

	/// <summary>The element a generated storyboard animates (set from <c>Storyboard.TargetName</c>); resolved with
	/// <see cref="TargetProperty"/> into <see cref="Apply"/>/<see cref="Release"/> before the storyboard runs.</summary>
	public UIElement? Target { get; set; }

	/// <summary>The property a generated storyboard animates (set from <c>Storyboard.TargetProperty</c>) — e.g.
	/// <c>Opacity</c>, <c>Background</c>, <c>RenderTransform.Translate.X</c>.</summary>
	public string? TargetProperty { get; set; }

	/// <summary>The animated value at normalized <paramref name="progress"/> (in <c>[0,1]</c>, un-eased).</summary>
	public abstract T ValueAt(double progress);

	/// <summary>Map a <c>TargetProperty</c> on <paramref name="target"/> to the typed apply/release for this animation
	/// kind (resolved inside the runtime, so the internal visual channels stay internal). An unknown property yields
	/// a no-op pair.</summary>
	protected abstract (Action<T> Apply, Action Release) ResolveChannel(UIElement target, string property);

	internal override void EnsureResolved()
	{
		if (Apply is not null || Target is null || TargetProperty is null)
			return;
		(Apply, Release) = ResolveChannel(Target, TargetProperty);
	}

	internal override void ApplyAt(double progress)
	{
		if (Apply is { } apply)
			apply(ValueAt(progress));
	}

	internal override void ReleaseTarget() => Release?.Invoke();
}
