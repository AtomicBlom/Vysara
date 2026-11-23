namespace BinaryVibrance.Vysara.Animation;

/// <summary>Power easing: <c>f(t) = tᵖ</c>, where <see cref="Power"/> <c>p</c> need not be whole.</summary>
public sealed class PowerEase : EasingFunctionBase
{
	/// <summary>The exponent. Defaults to 2; a negative power is taken as 0, as in WinUI.</summary>
	public double Power { get; set; } = 2.0;

	protected override double EaseCore(double t) => Math.Pow(t, Math.Max(Power, 0.0));
}
