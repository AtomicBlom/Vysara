namespace Vysara.Animation;

/// <summary>What an animation does to its target when it finishes.</summary>
public enum FillBehavior
{
	/// <summary>Hold the final animated value after the animation ends (the animation rung stays set).</summary>
	HoldEnd,

	/// <summary>Release the animated value, reverting the target to its underlying (Local/Style/Default) value.</summary>
	Stop,
}
