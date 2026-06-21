namespace SUIsei;

/// <summary>
/// Animates an <see cref="HdrColor"/> from <see cref="From"/> to <see cref="To"/> over the duration (PlanningUI.md
/// §D5.7). Interpolation is component-wise in the authored colour space (a cheap, deterministic lerp; perceptual
/// interpolation is a later refinement).
/// </summary>
public sealed class ColorAnimation : AnimationTimeline<HdrColor>
{
	/// <summary>The start colour (null ⇒ transparent).</summary>
	public HdrColor? From { get; set; }

	/// <summary>The end colour.</summary>
	public HdrColor? To { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	public override HdrColor ValueAt(double progress)
	{
		var from = From ?? HdrColor.Transparent;
		var to = To ?? from;
		var t = (float)(EasingFunction?.Ease(progress) ?? progress);
		return new HdrColor(
			from.R + (to.R - from.R) * t,
			from.G + (to.G - from.G) * t,
			from.B + (to.B - from.B) * t,
			from.A + (to.A - from.A) * t);
	}

	protected override (Action<HdrColor> Apply, Action Release) ResolveChannel(UIElement target, string property) => property switch
	{
		"Background" or "Foreground" or "Fill" => (target.SetAnimatedFill, target.ClearAnimatedFill),
		_ => (static _ => { }, static () => { }),
	};
}
