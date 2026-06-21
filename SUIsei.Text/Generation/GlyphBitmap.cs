namespace SUIsei.Text.Generation;

/// <summary>
/// A generated MTSDF glyph cell: linear RGBA in 0..1, row-major top-down (row 0 is the top of the cell). RGB hold
/// the multi-channel signed pseudo-distance; A holds the true signed distance. Composited into the atlas page and
/// quantized to bytes by the baker.
/// </summary>
internal sealed class GlyphBitmap(int width, int height)
{
	public int Width { get; } = width;
	public int Height { get; } = height;

	/// <summary>Width·Height·4 floats (R,G,B,A per texel), top-down rows.</summary>
	public float[] Pixels { get; } = new float[width * height * 4];
}
