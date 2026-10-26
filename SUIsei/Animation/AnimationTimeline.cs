namespace SUIsei.Animation;

/// <summary>
/// A <see cref="Timeline"/> that drives one target property. The <see cref="AnimationClock"/>
/// only needs to resolve the target, record where a run starts, push the current value and release the target on
/// stop, so the non-generic base exposes just those seams; the typed value and the apply/release/read delegates live
/// on <see cref="AnimationTimeline{T}"/>. The delegates are resolved to typed writes — no reflection, no boxing.
/// </summary>
public abstract class AnimationTimeline : Timeline
{
	/// <summary>Compute the value at normalized <paramref name="progress"/> (in <c>[0,1]</c>) and push it to the target.</summary>
	internal abstract void ApplyAt(double progress);

	/// <summary>Release the target (clear the animated value) — used on <see cref="FillBehavior.Stop"/> and on stop.</summary>
	internal abstract void ReleaseTarget();

	/// <summary>Resolve the declarative <c>Target</c>/<c>TargetProperty</c> (as set by generated XAML) into the typed
	/// apply/release/read, once, before the storyboard runs. A no-op when an explicit apply delegate was set in code.</summary>
	internal abstract void EnsureResolved();

	/// <summary>Record where this run starts — <c>From</c> when set, else the target's current value. Called as the
	/// animation begins, after <see cref="EnsureResolved"/>.</summary>
	internal abstract void CaptureStart();
}

/// <summary>
/// The typed animation: produces a <typeparamref name="T"/> at a given progress (<see cref="ValueAt"/>) and writes
/// it through <see cref="Apply"/>. <see cref="Release"/> reverts the target. Concrete subclasses (Double/Color/Point)
/// implement <see cref="ValueAt"/> and apply their own easing inside it.
/// <para>
/// The target is either <see cref="Target"/> plus <see cref="TargetProperty"/>, which bind the delegates when the
/// animation begins, or delegates set in code. As in WinUI, a run with no <c>From</c> starts at the value the target
/// shows when it begins — including the value of an animation it takes over from.
/// </para>
/// </summary>
public abstract class AnimationTimeline<T> : AnimationTimeline
{
	/// <summary>The typed write that pushes a value to the target (e.g. <c>v =&gt; el.SetAnimatedValue(OpacityProperty, (float)v)</c>).</summary>
	public Action<T>? Apply { get; set; }

	/// <summary>Reverts the target to its underlying value (e.g. <c>() =&gt; el.ClearAnimatedValue(OpacityProperty)</c>).</summary>
	public Action? Release { get; set; }

	/// <summary>
	/// Reads the target's current value, where a run with no <c>From</c> starts. Bound with <see cref="Apply"/> from
	/// <see cref="TargetProperty"/>; a code-first animation can set it beside <see cref="Apply"/>. Without one, such a
	/// run starts at <c>default</c>.
	/// </summary>
	public Func<T>? ReadCurrent { get; set; }

	/// <summary>The object a generated storyboard animates (set from <c>Storyboard.TargetName</c>); resolved with
	/// <see cref="TargetProperty"/> into <see cref="Apply"/>/<see cref="Release"/>/<see cref="ReadCurrent"/> when the
	/// animation begins.</summary>
	public DependencyObject? Target { get; set; }

	/// <summary>The property a generated storyboard animates on <see cref="Target"/> (resolved from
	/// <c>Storyboard.TargetProperty</c> at compile time).</summary>
	public PropertyPath? TargetProperty { get; set; }

	/// <summary>The target's value when the current run began (<see cref="ReadCurrent"/>): what a missing <c>From</c>
	/// stands for.</summary>
	private protected T StartValue { get; private set; } = default!;

	/// <summary>The animated value at normalized <paramref name="progress"/> (in <c>[0,1]</c>, un-eased).</summary>
	public abstract T ValueAt(double progress);

	/// <summary>Point <see cref="Apply"/>, <see cref="Release"/> and <see cref="ReadCurrent"/> at
	/// <paramref name="target"/> on <paramref name="owner"/>, converting between <typeparamref name="T"/> and its value
	/// type. False when this kind of animation cannot drive that type.</summary>
	private protected abstract bool TryBind(DependencyObject owner, IAnimationTarget target);

	/// <summary>Bind to a target whose value type is <typeparamref name="T"/> itself.</summary>
	internal bool BindDirect(DependencyObject owner, IAnimationTarget target)
	{
		if (target is not IAnimationTarget<T> typed)
			return false;
		Apply = value => typed.Write(owner, value);
		Release = () => typed.Release(owner);
		ReadCurrent = () => typed.Read(owner);
		return true;
	}

	internal override void EnsureResolved()
	{
		if (Apply is not null || Target is null || TargetProperty is null)
			return;
		var (owner, target) = TargetProperty.Resolve(Target);
		if (!TryBind(owner, target))
			throw new InvalidOperationException($"A {GetType().Name} cannot animate {target.Name}, which holds a {target.ValueTypeName}.");
	}

	internal override void CaptureStart() => StartValue = ReadCurrent is { } read ? read() : default!;

	internal override void ApplyAt(double progress)
	{
		if (Apply is { } apply)
			apply(ValueAt(progress));
	}

	internal override void ReleaseTarget() => Release?.Invoke();
}
