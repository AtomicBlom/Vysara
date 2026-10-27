namespace SUIsei.Animation;

/// <summary>Quartic easing: <c>f(t) = t⁴</c>.</summary>
public sealed class QuarticEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => t * t * t * t;
}
