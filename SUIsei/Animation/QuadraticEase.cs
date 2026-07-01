namespace SUIsei.Animation;

/// <summary>Quadratic easing: <c>f(t) = t²</c>.</summary>
public sealed class QuadraticEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => t * t;
}
