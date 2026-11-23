using BinaryVibrance.Vysara.Text.Generation;
using BinaryVibrance.Vysara.Text.Geometry;
using BinaryVibrance.Vysara.Text.OpenType;

namespace BinaryVibrance.Vysara.Text.Atlas;

/// <summary>
/// Mutable per-glyph scratch carried through the bake: the selected font + outline, then (after generation) the
/// cell bitmap, its packed position, and the EM plane bounds. Whitespace glyphs leave <see cref="Shape"/> null and
/// contribute only an advance.
/// </summary>
internal sealed class GlyphWork
{
	public int Codepoint;       // -1 for a glyph reached by id alone (e.g. a ligature with no code point)
	public int GlyphId;         // the font glyph index
	public double Advance;
	public Shape? Shape;
	public OpenTypeFont? Font;
	public GlyphBitmap? Bitmap;
	public int Width;
	public int Height;
	public int AtlasX;
	public int AtlasY;
	public double PlaneLeft;
	public double PlaneTop;
	public double PlaneRight;
	public double PlaneBottom;

	public bool HasGeometry => Shape is not null;
}
