namespace BinaryVibrance.Vysara.Animation;

/// <summary>
/// One key of a <see cref="DoubleAnimationUsingKeyFrames"/>: the scalar <see cref="Value"/> the
/// animation reaches at <see cref="KeyTime"/> (measured from the start of the animation), with an optional
/// <see cref="EasingFunction"/> shaping the segment that ends at this key.
/// </summary>
public sealed class DoubleKeyFrame
{
	/// <summary>When this key is reached, relative to the animation's start.</summary>
	public TimeSpan KeyTime { get; set; }

	/// <summary>The value at <see cref="KeyTime"/>.</summary>
	public double Value { get; set; }

	/// <summary>Eases the segment from the previous key to this one.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	/// <summary>Hold the previous key's value until <see cref="KeyTime"/>, then jump to <see cref="Value"/>, as WinUI's
	/// <c>DiscreteDoubleKeyFrame</c> does. <see cref="EasingFunction"/> is then ignored.</summary>
	public bool IsDiscrete { get; set; }
}
