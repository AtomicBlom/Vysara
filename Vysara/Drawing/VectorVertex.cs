using System.Numerics;

namespace Vysara.Drawing;

/// <summary>
/// One corner of a tessellated triangle: its position in the path's local space, the four signed distances a renderer
/// turns into antialiased coverage, and its arc length along the stroke.
/// </summary>
/// <param name="Position">Where the corner is, in the path's coordinates.</param>
/// <param name="Distances">
/// Four signed distances to the edges this triangle antialiases, in the path's units and positive inside; each is linear
/// across the triangle, so a renderer interpolates it and divides by its screen-space gradient to get pixels. A slot the
/// triangle does not use holds <see cref="Unused"/>, which reads as fully inside.
/// </param>
/// <param name="ArcLength">How far along the whole geometry's stroke this corner lies, all figures in order, as trim
/// measures it; 0 for a fill.</param>
public readonly record struct VectorVertex(Vector2 Position, Vector4 Distances, float ArcLength)
{
	/// <summary>
	/// The value of an unused distance slot. It is the same at every corner of the triangle, so its gradient is zero and
	/// the coverage it gives is 1. It is large and finite rather than infinite, so interpolating it never makes a NaN.
	/// </summary>
	public const float Unused = 1e6f;

	/// <summary>Four unused slots: a triangle drawn at full coverage, as a fill's interior is.</summary>
	public static Vector4 NoEdges => new(Unused);
}
