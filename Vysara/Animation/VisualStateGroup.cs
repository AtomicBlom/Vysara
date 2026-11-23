namespace Vysara.Animation;

/// <summary>
/// A set of mutually exclusive <see cref="VisualState"/>s — e.g. the <c>CommonStates</c> group
/// (<c>Normal</c>/<c>PointerOver</c>/<c>Pressed</c>). Exactly one state in a group is current at a time; entering a
/// new one stops the previous state's storyboard. Groups are independent of one another.
/// </summary>
public sealed class VisualStateGroup
{
	/// <summary>The group name (groups are addressed only indirectly, by the state name they contain).</summary>
	public required string Name { get; init; }

	/// <summary>The states in this group.</summary>
	public IList<VisualState> States { get; } = [];

	/// <summary>The state currently active in this group, if any.</summary>
	public VisualState? CurrentState { get; internal set; }

	/// <summary>The clock the current state's storyboard runs or holds its values on, which releases them when the state
	/// is left; null when the state was shown with no clock, its values written directly.</summary>
	internal AnimationClock? CurrentClock { get; set; }
}
