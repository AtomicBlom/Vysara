namespace BinaryVibrance.Vysara;

/// <summary>
/// One baked glyph from a <see cref="FontAtlas"/>. Plane bounds are EM units in a y-down,
/// baseline-at-origin frame (the on-screen quad, scaled by the font px size); the UV rect addresses the glyph's
/// MTSDF cell in the atlas page (0..1, v=0 at the top — the atlas is baked with <c>yOrigin: top</c>). Whitespace
/// glyphs set <see cref="HasGeometry"/> false and carry only an <see cref="Advance"/>.
/// <para><see cref="Codepoint"/> is the source Unicode code point (−1 for a glyph with none, e.g. a ligature in a
/// glyph-id-keyed atlas). <see cref="GlyphId"/> is the font glyph index (−1 in a codepoint-keyed atlas that does not
/// record ids) — the key a shaper such as HarfBuzz addresses glyphs by.</para>
/// </summary>
public readonly record struct Glyph(
	int Codepoint,
	int GlyphId,          // font glyph index; -1 when the atlas is codepoint-keyed only
	float Advance,        // EM units; pen advance after this glyph
	bool HasGeometry,     // false for whitespace (no quad, no atlas cell)
	float PlaneLeft,      // EM, relative to pen X / baseline (top is above baseline → negative)
	float PlaneTop,
	float PlaneRight,
	float PlaneBottom,
	float U0,             // atlas UV (top-left)
	float V0,
	float U1,             // atlas UV (bottom-right)
	float V1);
