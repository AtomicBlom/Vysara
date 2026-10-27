namespace SUIsei.Animation;

/// <summary>Circular easing: <c>f(t) = 1 − √(1 − t²)</c>, a quarter of a circle. As in WinUI, <c>t</c> is clamped to
/// <c>[−1, 1]</c>, so the root is always real.</summary>
public sealed class CircleEase : EasingFunctionBase
{
	protected override double EaseCore(double t)
	{
		t = Math.Clamp(t, -1.0, 1.0);
		return 1.0 - Math.Sqrt(1.0 - t * t);
	}
}
