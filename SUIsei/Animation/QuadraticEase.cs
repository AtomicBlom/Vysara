namespace SUIsei;

/// <summary>Quadratic easing: <c>f(t) = t²</c> (PlanningUI.md §D5.7).</summary>
public sealed class QuadraticEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => t * t;
}
