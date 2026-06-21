namespace SUIsei;

/// <summary>Cubic easing: <c>f(t) = t³</c> (PlanningUI.md §D5.7).</summary>
public sealed class CubicEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => t * t * t;
}
