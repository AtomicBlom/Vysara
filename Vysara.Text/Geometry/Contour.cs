namespace Vysara.Text.Geometry;

/// <summary>A closed loop of outline edges. A glyph is one or more contours (outer fill + inner holes).</summary>
internal sealed class Contour
{
	public List<EdgeSegment> Edges { get; } = [];

	public void Add(EdgeSegment edge) => Edges.Add(edge);

	/// <summary>Traverse the loop the other way: its edges in reverse order, each reversed.</summary>
	public void Reverse()
	{
		Edges.Reverse();
		for (var i = 0; i < Edges.Count; i++)
			Edges[i] = Edges[i].Reversed();
	}
}
