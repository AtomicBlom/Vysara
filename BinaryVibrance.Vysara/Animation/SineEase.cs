namespace BinaryVibrance.Vysara.Animation;

/// <summary>Sine easing: <c>f(t) = 1 − sin((1 − t)·π/2)</c>, a quarter of a sine wave. Its <see cref="EasingMode.EaseInOut"/>
/// is half a cosine wave, <c>(1 − cos(πt))/2</c>, with no sudden change in speed at either end.</summary>
public sealed class SineEase : EasingFunctionBase
{
	protected override double EaseCore(double t) => 1.0 - Math.Sin((1.0 - t) * (Math.PI / 2.0));
}
