using System.Numerics;

namespace Vysara.Composition;

/// <summary>The root of one surface's composition tree.</summary>
public sealed class RootVisual : Visual
{
	/// <summary>The surface's size in pixels.</summary>
	public Vector2 SurfaceSize { get; set => Change(ref field, value); }

	/// <summary>Whether the surface is HDR (drives colour linearization downstream).</summary>
	public bool IsHdrSurface { get; set => Change(ref field, value); }
}
