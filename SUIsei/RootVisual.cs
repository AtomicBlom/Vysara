using System.Numerics;

namespace SUIsei;

/// <summary>The root of one surface's composition tree (PlanningUI.md §D2.5).</summary>
public sealed class RootVisual : Visual
{
	public Vector2 SurfaceSize { get; set; }
	public bool IsHdrSurface { get; set; }
}
