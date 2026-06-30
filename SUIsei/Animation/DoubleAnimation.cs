namespace SUIsei;

/// <summary>
/// Animates a scalar from <see cref="From"/> to <see cref="To"/> (or by <see cref="By"/>) over the duration
/// A null <see cref="From"/> starts at zero; a null <see cref="To"/> with a
/// <see cref="By"/> ends at <c>From + By</c>.
/// </summary>
public sealed class DoubleAnimation : AnimationTimeline<double>
{
	/// <summary>The start value (null ⇒ 0).</summary>
	public double? From { get; set; }

	/// <summary>The end value (null with <see cref="By"/> ⇒ <c>From + By</c>).</summary>
	public double? To { get; set; }

	/// <summary>A relative end offset used when <see cref="To"/> is null.</summary>
	public double? By { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	public override double ValueAt(double progress)
	{
		var from = From ?? 0.0;
		var to = To ?? (By is { } by ? from + by : from);
		var eased = EasingFunction?.Ease(progress) ?? progress;
		return from + (to - from) * eased;
	}

	protected override (Action<double> Apply, Action Release) ResolveChannel(UIElement target, string property) => property switch
	{
		"Opacity" => (v => target.SetAnimatedValue(UIElement.OpacityProperty, (float)v), () => target.ClearAnimatedValue(UIElement.OpacityProperty)),
		"RenderTransform.Translate.X" => (v => target.SetAnimatedTranslateX((float)v), target.ClearAnimatedTranslate),
		"RenderTransform.Translate.Y" => (v => target.SetAnimatedTranslateY((float)v), target.ClearAnimatedTranslate),
		_ => (static _ => { }, static () => { }),
	};
}
