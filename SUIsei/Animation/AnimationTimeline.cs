namespace SUIsei.Animation;

/// <summary>
/// A <see cref="Timeline"/> that drives one target property. The <see cref="AnimationClock"/>
/// only needs to resolve the target, record where a run starts, push the current value and release the target on
/// stop, so the non-generic base exposes just those seams and the declarative target; the typed value and the
/// apply/release/read delegates live on <see cref="AnimationTimeline{T}"/>, and <see cref="ObjectAnimationUsingKeyFrames"/>
/// binds a typed write of its own. Either way the target is written through typed writes — no reflection, no boxing.
/// </summary>
public abstract class AnimationTimeline : Timeline
{
	/// <summary>The object a generated storyboard animates (set from <c>Storyboard.TargetName</c>); resolved with
	/// <see cref="TargetProperty"/> into a typed write when the animation first begins.</summary>
	public DependencyObject? Target { get; set; }

	/// <summary>The property a generated storyboard animates on <see cref="Target"/> (resolved from
	/// <c>Storyboard.TargetProperty</c> at compile time).</summary>
	public PropertyPath? TargetProperty { get; set; }

	/// <summary>Compute the value at normalized <paramref name="progress"/> (in <c>[0,1]</c>) and push it to the target.</summary>
	internal abstract void ApplyAt(double progress);

	/// <summary>Release the target (clear the animated value) — used on <see cref="FillBehavior.Stop"/> and on stop.</summary>
	internal abstract void ReleaseTarget();

	/// <summary>Resolve the declarative <c>Target</c>/<c>TargetProperty</c> (as set by generated XAML) into the typed
	/// apply/release/read, once, before the storyboard runs. A no-op when an explicit apply delegate was set in code.</summary>
	internal abstract void EnsureResolved();

	/// <summary>Throw if this run cannot begin: its target can't be bound, or a value it names is one the target refuses.
	/// Binds the target (<see cref="EnsureResolved"/>) but changes nothing a clock tracks or a target shows, so a clock
	/// checks every animation of a storyboard before it changes anything, and a storyboard that can't begin leaves
	/// everything as it was.</summary>
	internal abstract void CheckStart();

	/// <summary>Record where this run starts — <c>From</c> when set, else the target's current value. Called as the
	/// animation begins, after <see cref="CheckStart"/>; it throws nothing.</summary>
	internal abstract void CaptureStart();

	/// <summary>True between a <see cref="CheckStart"/> whose walk of the target path reached a different object than the
	/// last begin bound and the <see cref="CaptureStart"/> after it. Until then <see cref="ReleaseTarget"/> releases the
	/// object the earlier run set.</summary>
	internal abstract bool BindingMoved { get; }

	/// <summary>The object whose property this animation drives, once it has been bound to that property, as
	/// <see cref="Target"/> and <see cref="TargetProperty"/> bind it (<see cref="EnsureResolved"/>); null for one whose
	/// delegates were set in code.</summary>
	internal DependencyObject? BoundOwner { get; set; }

	/// <summary>The property on <see cref="BoundOwner"/> this animation drives; null for one whose delegates were set in
	/// code.</summary>
	internal IAnimationTarget? BoundProperty { get; set; }

	/// <summary>Whether this animation and <paramref name="other"/> were bound to the same property of the same object,
	/// which is how the <see cref="AnimationClock"/> hands a property from one animation to the next. Animations whose
	/// delegates were set in code cannot be compared, so never are.</summary>
	internal bool DrivesSamePropertyAs(AnimationTimeline other) =>
		BoundProperty is not null && ReferenceEquals(BoundProperty, other.BoundProperty) && ReferenceEquals(BoundOwner, other.BoundOwner);
}

/// <summary>
/// The typed animation: produces a <typeparamref name="T"/> at a given progress (<see cref="ValueAt"/>) and writes
/// it through <see cref="Apply"/>. <see cref="Release"/> reverts the target. Concrete subclasses (Double/Color/Point)
/// implement <see cref="ValueAt"/> and apply their own easing inside it.
/// <para>
/// The target is either <see cref="AnimationTimeline.Target"/> plus <see cref="AnimationTimeline.TargetProperty"/>, which
/// bind the delegates when the animation begins, or delegates set in code. As in WinUI, a run with no <c>From</c> starts at the value the target
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
	/// <see cref="AnimationTimeline.TargetProperty"/>; a code-first animation can set it beside <see cref="Apply"/>.
	/// Without one, such a run starts at <c>default</c>.
	/// </summary>
	public Func<T>? ReadCurrent { get; set; }

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
		ValidateValue = typed.Validates ? value => typed.Validate(owner, value) : null;
		(BoundOwner, BoundProperty) = (owner, target);
		return true;
	}

	// The path is walked again on every begin, as WinUI walks it: a path through objects (a border's background brush)
	// reaches whatever object is there now, so a storyboard never animates a brush that has since been replaced, which may
	// be a resource shared with other elements. The delegates are bound again only when the walk reaches a different
	// object or property, so beginning the same state again allocates nothing.
	internal override void EnsureResolved()
	{
		if (Target is null || TargetProperty is null || (Apply is not null && BoundOwner is null))
			return; // nothing declared, or delegates set in code
		var (owner, target) = TargetProperty.Resolve(Target, interpolates: true);
		if (ReferenceEquals(owner, BoundOwner) && ReferenceEquals(target, BoundProperty))
			return;
		var before = BoundOwner is null ? null : Release;
		if (!TryBind(owner, target))
			throw new InvalidOperationException($"A {GetType().Name} cannot animate {target.Name}, which holds a {target.ValueTypeName}.");
		_releaseMoved ??= before;
	}

	// The release of the object an earlier begin bound, kept from the moment a begin's walk reached a different one until
	// that begin records its start: what the earlier run set there is released through it.
	private Action? _releaseMoved;

	internal override bool BindingMoved => _releaseMoved is not null;

	internal override void CheckStart()
	{
		EnsureResolved();
		StartValue = ReadCurrent is { } read ? read() : default!; // the start a missing From stands for, as it is now
		if (ValidateValue is { } validate)
			ValidateValues(validate);
	}

	internal override void CaptureStart()
	{
		_releaseMoved = null;
		StartValue = ReadCurrent is { } read ? read() : default!;
	}

	/// <summary>
	/// Checks a value against the target property's own rules (its <see cref="PropertyMetadata{TOwner, TValue}.Validate"/>),
	/// bound with <see cref="Apply"/> from <see cref="AnimationTimeline.TargetProperty"/>; null for a property with none,
	/// and for an animation whose delegates were set in code.
	/// </summary>
	internal Action<T>? ValidateValue { get; set; }

	/// <summary>Check the values this animation names as it begins, through <paramref name="validate"/>: its start and its
	/// end, and an animation with keyframes each of them. What it produces between them is not checked, so an easing's
	/// overshoot never throws mid-animation; nor is a start read from the target (<see cref="StartsFromTarget"/>), which
	/// is whatever is on screen, an earlier animation's overshoot included.</summary>
	private protected virtual void ValidateValues(Action<T> validate)
	{
		if (!StartsFromTarget)
			validate(ValueAt(0d));
		validate(ValueAt(1d));
	}

	/// <summary>True when the run starts from the target's value rather than one the animation names (no <c>From</c>).</summary>
	private protected virtual bool StartsFromTarget => false;

	internal override void ApplyAt(double progress)
	{
		if (Apply is { } apply)
			apply(ValueAt(progress));
	}

	internal override void ReleaseTarget() => (_releaseMoved ?? Release)?.Invoke();
}
