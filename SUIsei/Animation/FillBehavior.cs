namespace SUIsei;

/// <summary>What an animation does to its target when it finishes (PlanningUI.md §D5.7).</summary>
public enum FillBehavior
{
	/// <summary>Hold the final animated value after the animation ends (the animation rung stays set).</summary>
	HoldEnd,

	/// <summary>Release the animated value, reverting the target to its underlying (Local/Style/Default) value.</summary>
	Stop,
}
