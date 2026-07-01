namespace SUIsei.Animation;

/// <summary>
/// The base of the built-in easing functions. A subclass defines the raw ease-in shape in
/// <see cref="EaseCore"/>; this base mirrors it for <see cref="EasingMode.EaseOut"/> and splices the two halves for
/// <see cref="EasingMode.EaseInOut"/>, matching WinUI.
/// </summary>
public abstract class EasingFunctionBase : IEasingFunction
{
	/// <summary>Which end of the curve is shaped. Defaults to <see cref="EasingMode.EaseOut"/> (WinUI's default).</summary>
	public EasingMode EasingMode { get; set; } = EasingMode.EaseOut;

	/// <summary>The normalized ease-in shape: <c>f(0)=0</c>, <c>f(1)=1</c>, accelerating.</summary>
	protected abstract double EaseCore(double t);

	public double Ease(double t) => EasingMode switch
	{
		EasingMode.EaseIn => EaseCore(t),
		EasingMode.EaseOut => 1.0 - EaseCore(1.0 - t),
		_ => t < 0.5 ? EaseCore(2.0 * t) * 0.5 : 1.0 - EaseCore(2.0 - 2.0 * t) * 0.5,
	};
}
