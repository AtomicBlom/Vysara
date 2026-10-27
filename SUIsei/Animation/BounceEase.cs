namespace SUIsei.Animation;

/// <summary>
/// Bounce easing, as WinUI's: in the default <see cref="EasingMode.EaseOut"/> a ball dropped onto the end value, bouncing
/// <see cref="Bounces"/> times, each bounce <see cref="Bounciness"/> times lower and shorter than the one before. Each
/// bounce
/// is a parabola; their widths form a geometric series whose sum, with half the last, spans the whole curve.
/// </summary>
public sealed class BounceEase : EasingFunctionBase
{
	/// <summary>How many times it bounces before it settles. Defaults to 3; a negative count is taken as 0.</summary>
	public int Bounces { get; set; } = 3;

	/// <summary>
	/// How much lower each bounce is than the one before, and so how much shorter. Defaults to 2; anything up to 1
	/// (within
	/// WinUI's epsilon) is taken as 1.01, as in WinUI, since bounces that never shrink never end.
	/// </summary>
	public double Bounciness { get; set; } = 2.0;

	protected override double EaseCore(double t)
	{
		var bounces = (double)Math.Max(Bounces, 0);
		var b = Bounciness <= 1.0 + 1e-4 ? 1.01 : Bounciness;

		// Units: the first bounce is one unit wide and each later one b times wider. Find the unit t falls in.
		var pow = Math.Pow(b, bounces);
		var oneMinusB = 1.0 - b;
		var sumOfUnits = (1.0 - pow) / oneMinusB + pow / 2.0;
		var unitAtT = t * sumOfUnits;

		// Bounces: solve unitAtT = (1 − bᵏ)/(1 − b) for k, then project that bounce's ends back into time.
		var bounceAtT = Math.Log(-unitAtT * oneMinusB + 1.0) / Math.Log(b);
		var start = Math.Floor(bounceAtT);
		var startTime = (1.0 - Math.Pow(b, start)) / (oneMinusB * sumOfUnits);
		var endTime = (1.0 - Math.Pow(b, start + 1.0)) / (oneMinusB * sumOfUnits);

		// The parabola through (startTime, 0) and (endTime, 0), peaking at the bounce's height.
		var midTime = (startTime + endTime) / 2.0;
		var fromPeak = t - midTime;
		var radius = midTime - startTime;
		var amplitude = Math.Pow(1.0 / b, bounces - start);
		return -amplitude / (radius * radius) * (fromPeak - radius) * (fromPeak + radius);
	}
}
