namespace SUIsei.Animation;

/// <summary>
/// Sets a property of any type to the value of each of its <see cref="KeyFrames"/> in turn, as WinUI's
/// <c>ObjectAnimationUsingKeyFrames</c>: how a visual state collapses what it fades out, or shows a brush, a string or an
/// enum member of its own. Nothing is interpolated, so it drives any dependency property, animatable or not.
/// <para>
/// Each key's <see cref="ObjectKeyFrame.Value"/> holds from its <see cref="ObjectKeyFrame.KeyTime"/>, measured against the
/// <see cref="Timeline.Duration"/> as <see cref="DoubleAnimationUsingKeyFrames"/>'s are, until a later key's; keys need not
/// be in order. Before the first, the property holds the value it showed when the animation began. The values are written
/// to the animation rung, so stopping the animation (leaving its visual state) shows the property's own value again. A
/// <see cref="Timeline.Duration"/> of zero shows the last key at once, which is how
/// <see cref="VisualStateManager"/>'s snap, with transitions off, shows it too. The XAML generator sets the duration to
/// the last key's time when markup gives none, as WinUI does; built in code, set it so.
/// </para>
/// <para>
/// The target is <see cref="AnimationTimeline.Target"/> and <see cref="AnimationTimeline.TargetProperty"/>. Each value must
/// be of the property's type, or null where it takes null: the generator converts them at compile time, as it does a
/// setter's, and one that is not is an <see cref="InvalidOperationException"/> when the animation begins, as is a value
/// the property's own validation refuses. The values are held as objects but written unboxed, so a running animation, and
/// beginning one again, allocates nothing.
/// </para>
/// </summary>
public sealed class ObjectAnimationUsingKeyFrames : AnimationTimeline
{
	// The typed write to the target, made when the animation first begins.
	private ObjectBinding? _binding;

	/// <summary>The keys. Each holds from its <see cref="ObjectKeyFrame.KeyTime"/> until a later key's.</summary>
	public IList<ObjectKeyFrame> KeyFrames { get; } = [];

	/// <summary>WinUI's opt-in for an animation of a property that relayouts; SUIsei runs every animation and reads nothing
	/// here (<see cref="DoubleAnimation.EnableDependentAnimation"/>).</summary>
	public bool EnableDependentAnimation { get; set; }

	internal override void EnsureResolved()
	{
		if (Target is null || TargetProperty is null)
			return;
		// Walked again on every begin, so a replaced object along the path is the one animated; bound again only when the
		// walk reaches a different one (AnimationTimeline<T>.EnsureResolved).
		var (owner, target) = TargetProperty.Resolve(Target, interpolates: false);
		if (_binding is not null && ReferenceEquals(owner, BoundOwner) && ReferenceEquals(target, BoundProperty))
			return;
		_bindingMoved ??= _binding;
		_binding = target.BindObjects(owner);
		(BoundOwner, BoundProperty) = (owner, target);
	}

	// Its keys are checked, not where it starts: before the first key the property holds whatever it showed as the run began.
	internal override void CheckStart(bool released)
	{
		if (!_checking)
		{
			_beforeCheck = (_binding, _bindingMoved, BoundOwner, BoundProperty);
			_checking = true;
		}
		EnsureResolved();
		if (_binding is not { } binding)
			return;
		ThrowIfSealed();
		for (var i = 0; i < KeyFrames.Count; i++)
		{
			var value = KeyFrames[i].Value;
			if (!binding.Accepts(value))
				throw new InvalidOperationException(
					$"An ObjectAnimationUsingKeyFrames cannot set {binding.Property.Name}, which holds a {binding.Property.ValueTypeName}, " +
					$"to {(value is null ? "null" : "a " + value.GetType().Name)} (its key at {KeyFrames[i].KeyTime}).");
			binding.Validate(value);
		}
	}

	// The binding the animation had before the pending begin's first check, put back if that begin is given up.
	private (ObjectBinding? Binding, ObjectBinding? Moved, DependencyObject? Owner, IAnimationTarget? Property) _beforeCheck;

	private bool _checking;

	internal override void AbandonStart()
	{
		if (!_checking)
			return;
		(_binding, _bindingMoved, BoundOwner, BoundProperty) = _beforeCheck;
		_beforeCheck = default;
		_checking = false;
	}

	internal override void CaptureStart()
	{
		_beforeCheck = default;
		_checking = false;
		_bindingMoved = null;
		_binding?.CaptureStart();
	}

	// The binding an earlier begin made, kept while a begin whose walk reached a different object is under way, so what the
	// earlier run set is released there (AnimationTimeline.BindingMoved).
	private ObjectBinding? _bindingMoved;

	internal override bool BindingMoved => _bindingMoved is not null;

	internal override void ApplyAt(double progress)
	{
		if (_binding is not { } binding)
			return;
		if (KeyAt(progress) is { } key)
			binding.Write(key.Value);
		else
			binding.WriteStart();
	}

	internal override void ReleaseTarget() => (_bindingMoved ?? _binding)?.Release();

	// The key showing at normalized `progress`: of those reached, the one with the latest KeyTime (the last listed of
	// equal ones). Null before the first.
	private ObjectKeyFrame? KeyAt(double progress)
	{
		var d = Duration.TotalSeconds;
		ObjectKeyFrame? showing = null;
		for (var i = 0; i < KeyFrames.Count; i++)
		{
			var key = KeyFrames[i];
			var reached = d <= 0 || key.KeyTime.TotalSeconds / d <= progress;
			if (reached && (showing is null || key.KeyTime >= showing.KeyTime))
				showing = key;
		}
		return showing;
	}
}
