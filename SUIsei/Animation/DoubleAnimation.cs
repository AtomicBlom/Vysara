namespace SUIsei.Animation;

/// <summary>
/// Animates a scalar from <see cref="From"/> to <see cref="To"/> (or by <see cref="By"/>) over the duration. A null
/// <see cref="From"/> starts at the target's value when the animation begins; a null <see cref="To"/> with a
/// <see cref="By"/> ends at <c>From + By</c>. Drives a float or double property.
/// </summary>
public sealed class DoubleAnimation : AnimationTimeline<double>
{
	/// <summary>The start value (null ⇒ the target's value when the animation begins).</summary>
	public double? From { get; set; }

	/// <summary>The end value (null with <see cref="By"/> ⇒ <c>From + By</c>).</summary>
	public double? To { get; set; }

	/// <summary>A relative end offset used when <see cref="To"/> is null.</summary>
	public double? By { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	public override double ValueAt(double progress)
	{
		var from = From ?? StartValue;
		var to = To ?? (By is { } by ? from + by : from);
		var eased = EasingFunction?.Ease(progress) ?? progress;
		return from + (to - from) * eased;
	}

	private protected override bool TryBind(DependencyObject owner, IAnimationTarget target) => DoubleBinding.TryBind(this, owner, target);
}
