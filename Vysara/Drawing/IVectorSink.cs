namespace Vysara.Drawing;

/// <summary>
/// Receives the triangles a <see cref="TessellatedPath"/> draws when emitted: the draw list's vector emitter, or
/// a foreign compositor's own buffer. Implemented by a struct, it is called with no virtual dispatch and no
/// allocation.
/// </summary>
public interface IVectorSink
{
	/// <summary>Add whole triangles, three vertices each. The span is only valid during the call.</summary>
	void AddTriangles(ReadOnlySpan<VectorVertex> vertices);
}
