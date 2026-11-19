namespace SUIsei.Controls.Primitives;

/// <summary>How a flyout is shown, as WinUI's <c>FlyoutShowOptions</c>: at a point in its target (a context menu at the
/// pointer), or against the whole target where <see cref="Position"/> is null; on <see cref="Placement"/>'s side, or the
/// flyout's own.</summary>
public sealed class FlyoutShowOptions
{
	/// <summary>The point in the target's own coordinates the flyout opens at, or null for the whole target.</summary>
	public Point? Position { get; set; }

	/// <summary>The side to open on, or null for the flyout's <see cref="FlyoutBase.Placement"/>.</summary>
	public FlyoutPlacementMode? Placement { get; set; }
}
