namespace BinaryVibrance.Vysara.Animation;

/// <summary>
/// Animates a scalar through a sequence of <see cref="DoubleKeyFrame"/>s. Each key's
/// <see cref="DoubleKeyFrame.KeyTime"/> is normalized against the <see cref="Timeline.Duration"/>; the value before
/// the first key holds at its value, the value after the last key holds at its value, and each segment is lerped with
/// the upper key's own easing. Keyframes are assumed to be in ascending <see cref="DoubleKeyFrame.KeyTime"/> order.
/// </summary>
public sealed class DoubleAnimationUsingKeyFrames : AnimationTimeline<double>
{
	/// <summary>The keys, in ascending <see cref="DoubleKeyFrame.KeyTime"/> order.</summary>
	public IList<DoubleKeyFrame> KeyFrames { get; } = [];

	/// <summary>WinUI's opt-in for an animation of a property that relayouts; Vysara runs every animation and reads nothing
	/// here (<see cref="DoubleAnimation.EnableDependentAnimation"/>).</summary>
	public bool EnableDependentAnimation { get; set; }

	public override double ValueAt(double progress)
	{
		var n = KeyFrames.Count;
		if (n == 0)
			return 0.0;

		var d = Duration.TotalSeconds;
		if (d <= 0)
			return KeyFrames[n - 1].Value;

		var first = KeyFrames[0];
		if (progress <= first.KeyTime.TotalSeconds / d)
			return first.Value;

		for (var i = 1; i < n; i++)
		{
			var lo = KeyFrames[i - 1];
			var hi = KeyFrames[i];
			var hiPos = hi.KeyTime.TotalSeconds / d;
			if (progress <= hiPos)
			{
				var loPos = lo.KeyTime.TotalSeconds / d;
				var span = hiPos - loPos;
				var local = span > 0 ? (progress - loPos) / span : 1.0;
				var eased = hi.IsDiscrete ? (local >= 1.0 ? 1.0 : 0.0) : hi.EasingFunction?.Ease(local) ?? local;
				return lo.Value + (hi.Value - lo.Value) * eased;
			}
		}

		return KeyFrames[n - 1].Value;
	}

	private protected override void ValidateValues(Action<double> validate)
	{
		for (var i = 0; i < KeyFrames.Count; i++)
			validate(KeyFrames[i].Value);
	}

	private protected override bool TryBind(DependencyObject owner, IAnimationTarget target) => DoubleBinding.TryBind(this, owner, target);
}
