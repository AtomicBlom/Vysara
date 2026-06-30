namespace SUIsei;

/// <summary>Animates a <see cref="Point"/> from <see cref="From"/> to <see cref="To"/> over the duration.</summary>
public sealed class PointAnimation : AnimationTimeline<Point>
{
	/// <summary>The start point (null ⇒ origin).</summary>
	public Point? From { get; set; }

	/// <summary>The end point.</summary>
	public Point? To { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	public override Point ValueAt(double progress)
	{
		var from = From ?? new Point(0f, 0f);
		var to = To ?? from;
		var t = (float)(EasingFunction?.Ease(progress) ?? progress);
		return new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
	}

	// A point has no built-in animatable channel; a target must supply an explicit apply closure.
	protected override (Action<Point> Apply, Action Release) ResolveChannel(UIElement target, string property) =>
		(static _ => { }, static () => { });
}
