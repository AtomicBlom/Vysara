namespace SUIsei.Animation;

/// <summary>
/// Animates an <see cref="HdrColor"/> from <see cref="From"/> to <see cref="To"/> over the duration.
/// Interpolation is component-wise in the authored colour space (a cheap, deterministic lerp).
/// </summary>
public sealed class ColorAnimation : AnimationTimeline<HdrColor>
{
	/// <summary>The start colour (null ⇒ the target's colour when the animation begins).</summary>
	public HdrColor? From { get; set; }

	/// <summary>The end colour.</summary>
	public HdrColor? To { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	/// <summary>WinUI's opt-in for an animation of a property that relayouts; SUIsei runs every animation and reads nothing
	/// here (<see cref="DoubleAnimation.EnableDependentAnimation"/>).</summary>
	public bool EnableDependentAnimation { get; set; }

	public override HdrColor ValueAt(double progress)
	{
		var from = From ?? StartValue;
		var to = To ?? from;
		var t = (float)(EasingFunction?.Ease(progress) ?? progress);
		return Lerps.Color(from, to, t);
	}

	private protected override bool StartsFromTarget => From is null;

	private protected override bool TryBind(DependencyObject owner, IAnimationTarget target) => BindDirect(owner, target);
}
