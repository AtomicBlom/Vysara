using System.Numerics;

namespace SUIsei;

/// <summary>
/// Where and how a <see cref="UIView"/> is placed (PlanningUI.md §D5.1). MU2 implements <see cref="Overlay"/> —
/// a screen-space layer with a z-order and a DIP→pixel <see cref="LayoutScale"/>. <see cref="InWorld"/> (a quad with
/// a world transform + optional billboard, plus its 3D ray hit-test) is declared for API shape but lands in MU5.
/// </summary>
public readonly record struct UIPlacement
{
	private UIPlacement(UIPlacementKind kind, int zLayer, float layoutScale)
	{
		Kind = kind;
		ZLayer = zLayer;
		LayoutScale = layoutScale;
	}

	/// <summary>Overlay or in-world.</summary>
	public UIPlacementKind Kind { get; }

	/// <summary>Screen-layer order — higher is on top (drives hit-test priority).</summary>
	public int ZLayer { get; }

	/// <summary>DIP→pixel scale: window DPI/96 for an overlay (PlanningUI.md §D2.8).</summary>
	public float LayoutScale { get; }

	/// <summary>A screen-space overlay layer.</summary>
	public static UIPlacement Overlay(int zLayer = 0, float layoutScale = 1f) =>
		new(UIPlacementKind.Overlay, zLayer, layoutScale);

	/// <summary>An interactive in-world quad — deferred to MU5 (kept here for API shape).</summary>
	public static UIPlacement InWorld(in Matrix4x4 worldTransform, Vector2 sizeMeters, bool billboard, float texelDensity) =>
		throw new NotSupportedException("In-world UI placement (and its 3D ray hit-test) lands in MU5.");
}
