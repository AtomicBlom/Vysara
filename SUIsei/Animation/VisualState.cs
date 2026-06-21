namespace SUIsei;

/// <summary>
/// One named look a control can be in (PlanningUI.md §D5.7) — e.g. <c>Normal</c>, <c>PointerOver</c>, <c>Pressed</c>.
/// Entering the state runs its <see cref="Storyboard"/> (a null storyboard is the resting/default look).
/// </summary>
public sealed class VisualState
{
	/// <summary>The state name matched by <see cref="VisualStateManager.GoToState"/>.</summary>
	public required string Name { get; init; }

	/// <summary>The storyboard run when this state is entered; null means "no animation" (the base look).</summary>
	public Storyboard? Storyboard { get; init; }
}
