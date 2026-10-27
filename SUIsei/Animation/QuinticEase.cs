namespace SUIsei.Animation;

/// <summary>Quintic easing: <c>f(t) = t⁵</c>.</summary>
public sealed class QuinticEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => t * t * t * t * t;
}
