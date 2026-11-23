namespace BinaryVibrance.Vysara;

/// <summary>
/// A screen-space compositing layer for a <see cref="UIView"/>: <see cref="ZLayer"/> sets hit-test priority
/// (higher is on top) and <see cref="LayoutScale"/> is the DIP→pixel ratio (window DPI/96 for a normal overlay).
/// In-world placement is handled externally by <c>InWorldPanel</c> in <c>BinaryVibrance.Kaldra.UI.Vysara</c>, which wraps its own
/// <c>UIHost</c> with a <c>UILayer</c> and projects screen-space pointer events onto the panel's world-space quad —
/// Vysara itself remains a pure 2D system.
/// </summary>
public readonly record struct UILayer(int ZLayer = 0, float LayoutScale = 1f)
{
	public static readonly UILayer Default = new(0);
};
