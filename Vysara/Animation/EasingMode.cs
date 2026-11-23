namespace Vysara.Animation;

/// <summary>Which end of an easing function is shaped.</summary>
public enum EasingMode
{
	/// <summary>Apply the easing curve at the start.</summary>
	EaseIn,

	/// <summary>Apply the easing curve at the end (the WinUI default).</summary>
	EaseOut,

	/// <summary>Ease in over the first half and out over the second.</summary>
	EaseInOut,
}
