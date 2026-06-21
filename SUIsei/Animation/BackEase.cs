namespace SUIsei;

/// <summary>
/// Back easing: overshoots slightly before settling (PlanningUI.md §D5.7). <c>f(t) = t³ − t·A·sin(πt)</c>, where
/// <see cref="Amplitude"/> <c>A</c> scales the overshoot.
/// </summary>
public sealed class BackEase : EasingFunctionBase
{
	/// <summary>How far the curve overshoots (≥ 0). Defaults to 1.</summary>
	public double Amplitude { get; set; } = 1.0;

	protected override double EaseCore(double t)
	{
		var a = Amplitude < 0 ? 0 : Amplitude;
		return t * t * t - t * a * Math.Sin(Math.PI * t);
	}
}
