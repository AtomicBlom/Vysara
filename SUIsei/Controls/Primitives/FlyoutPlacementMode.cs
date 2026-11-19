namespace SUIsei.Controls.Primitives;

/// <summary>
/// Where a flyout opens against its target, as WinUI's <c>FlyoutPlacementMode</c>: on a side, centred on the target or
/// with an edge lined up with the target's, turned to the other side where there is no room. <see cref="Auto"/> is
/// <see cref="Bottom"/>. <see cref="Full"/>, covering the window, is not provided: a flyout set to it throws as it opens.
/// </summary>
public enum FlyoutPlacementMode
{
	Top,
	Bottom,
	Left,
	Right,
	Full,
	TopEdgeAlignedLeft,
	TopEdgeAlignedRight,
	BottomEdgeAlignedLeft,
	BottomEdgeAlignedRight,
	LeftEdgeAlignedTop,
	LeftEdgeAlignedBottom,
	RightEdgeAlignedTop,
	RightEdgeAlignedBottom,
	Auto,
}
