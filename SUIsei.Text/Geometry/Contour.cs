namespace SUIsei.Text.Geometry;

/// <summary>A closed loop of outline edges. A glyph is one or more contours (outer fill + inner holes).</summary>
internal sealed class Contour
{
	public List<EdgeSegment> Edges { get; } = [];

	public void Add(EdgeSegment edge) => Edges.Add(edge);
}
