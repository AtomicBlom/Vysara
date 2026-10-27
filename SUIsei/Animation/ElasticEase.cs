namespace SUIsei.Animation;

/// <summary>
/// Elastic easing, as WinUI's: a spring, which in the default <see cref="EasingMode.EaseOut"/> overshoots the end
/// value
/// and swings back and forth across it, settling as its swings die away. <c>f(t) = r(t)·sin((2πn + π/2)·t)</c>,
/// where
/// <c>n</c> is <see cref="Oscillations"/> and the envelope <c>r</c> is <see cref="ExponentialEase"/>'s curve with
/// <see cref="Springiness"/> as its exponent.
/// </summary>
public sealed class ElasticEase : EasingFunctionBase
{
	/// <summary>
	/// How many times the spring swings across the end value. Defaults to 3; a negative count is taken as 0.
	/// </summary>
	public int Oscillations { get; set; } = 3;

	/// <summary>
	/// How stiff the spring is: the larger, the faster its swings die away. Defaults to 3; at 0 they shrink linearly.
	/// </summary>
	public double Springiness { get; set; } = 3.0;

	protected override double EaseCore(double t)
	{
		var oscillations = Math.Max(Oscillations, 0);
		return ExponentialRamp(t, Springiness) * Math.Sin(t * (2.0 * Math.PI * oscillations + Math.PI / 2.0));
	}
}
