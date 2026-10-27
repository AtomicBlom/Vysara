namespace SUIsei.Animation;

/// <summary>
/// One key of an <see cref="ObjectAnimationUsingKeyFrames"/>: the <see cref="Value"/> its property takes at
/// <see cref="KeyTime"/>, measured from the start of the animation. As in WinUI, the only kind is
/// <see cref="DiscreteObjectKeyFrame"/>: a value of any type cannot be interpolated, only set.
/// </summary>
public abstract class ObjectKeyFrame
{
	private protected ObjectKeyFrame()
	{
	}

	/// <summary>When this key is reached, relative to the animation's start.</summary>
	public TimeSpan KeyTime { get; set; }

	/// <summary>The value at <see cref="KeyTime"/>: one of the animated property's type, or null where it takes null.</summary>
	public object? Value { get; set; }
}
