namespace Vysara.Animation;

/// <summary>Exponential easing: <c>f(t) = (eᵃᵗ − 1)/(eᵃ − 1)</c>, where <see cref="Exponent"/> <c>a</c> sets how sharply
/// it accelerates. A negative exponent turns the curve over, so that it decelerates instead.</summary>
public sealed class ExponentialEase : EasingFunctionBase
{
	/// <summary>The exponent. Defaults to 2; at 0 (within WinUI's epsilon) the curve is the straight line it tends to.</summary>
	public double Exponent { get; set; } = 2.0;

	protected override double EaseCore(double t) => ExponentialRamp(t, Exponent);
}
