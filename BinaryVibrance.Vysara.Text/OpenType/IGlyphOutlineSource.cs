using BinaryVibrance.Vysara.Text.Geometry;

namespace BinaryVibrance.Vysara.Text.OpenType;

/// <summary>
/// The outline-format seam: yields a glyph's contours in font units (y-up). TrueType <c>glyf</c> and OpenType
/// <c>CFF</c> each implement it, so the rest of the pipeline (colouring, MTSDF generation, packing) is outline-format
/// agnostic. A GPU outline source could implement the same contract later.
/// </summary>
internal interface IGlyphOutlineSource
{
	/// <summary>Build the outline of <paramref name="glyphId"/> as a <see cref="Shape"/>, or an empty shape for a blank glyph.</summary>
	Shape GetOutline(int glyphId);
}
