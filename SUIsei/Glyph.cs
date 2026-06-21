namespace SUIsei;

/// <summary>
/// One baked glyph from a <see cref="FontAtlas"/> (PlanningUI.md §D3). Plane bounds are EM units in a y-down,
/// baseline-at-origin frame (the on-screen quad, scaled by the font px size); the UV rect addresses the glyph's
/// MTSDF cell in the atlas page (0..1, v=0 at the top — the atlas is baked with <c>yOrigin: top</c>). Whitespace
/// glyphs set <see cref="HasGeometry"/> false and carry only an <see cref="Advance"/>.
/// </summary>
public readonly record struct Glyph(
	int Codepoint,
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
