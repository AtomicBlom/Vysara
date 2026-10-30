namespace SUIsei.Animation;

/// <summary>Animates a <see cref="Point"/> from <see cref="From"/> to <see cref="To"/> over the duration.</summary>
public sealed class PointAnimation : AnimationTimeline<Point>
{
	/// <summary>The start point (null ⇒ the target's point when the animation begins).</summary>
	public Point? From { get; set; }

	/// <summary>The end point.</summary>
	public Point? To { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	public override Point ValueAt(double progress)
	{
		var from = From ?? StartValue;
		var to = To ?? from;
		var t = (float)(EasingFunction?.Ease(progress) ?? progress);
		return Lerps.Point(from, to, t);
	}

	private protected override bool StartsFromTarget => From is null;

	private protected override bool TryBind(DependencyObject owner, IAnimationTarget target) => BindDirect(owner, target);
}
