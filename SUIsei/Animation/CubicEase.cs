namespace SUIsei;

/// <summary>Cubic easing: <c>f(t) = t³</c>.</summary>
public sealed class CubicEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => t * t * t;
}
