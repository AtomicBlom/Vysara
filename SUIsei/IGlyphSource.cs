namespace SUIsei;

/// <summary>
/// The font read-surface a text shaper (and any glyph consumer) needs, independent of how glyphs are imaged:
/// code-point and glyph-id lookups returning the drawable <see cref="Glyph"/> cell, advances, and EM line metrics.
/// <see cref="FontAtlas"/> is the MTSDF implementation; a different imagery backend (e.g. a bitmap atlas) can
/// implement the same interface, so the shaping backend (<see cref="ITextShaper"/>) and the imagery backend swap
/// independently — a shaper is written against this interface, never a concrete atlas. Render-technique specifics
/// (e.g. an MTSDF distance range) are deliberately kept off this interface.
/// </summary>
public interface IGlyphSource
{
	/// <summary>Look up a glyph by Unicode code point. Returns false for code points outside the baked charset.</summary>
	bool TryGetGlyph(int codepoint, out Glyph glyph);

	/// <summary>Look up a glyph by font glyph id — the key a shaper emits (including ligature/contextual glyphs with
	/// no code point). Only meaningful when <see cref="HasGlyphIds"/>.</summary>
	bool TryGetGlyphById(int glyphId, out Glyph glyph);

	/// <summary>Map a Unicode code point to its font glyph id (the cmap), when ids are available.</summary>
	bool TryGetGlyphId(int codepoint, out int glyphId);

	/// <summary>True when glyphs can be addressed by id, so an id-emitting shaper (e.g. HarfBuzz) can resolve here.</summary>
	bool HasGlyphIds { get; }

	/// <summary>Advance width of a code point in EM.</summary>
	float Advance(int codepoint);

	/// <summary>Ascender in EM (y-down: above the baseline, so negative).</summary>
	float Ascender { get; }

	/// <summary>Line advance in EM.</summary>
	float LineHeight { get; }
}
