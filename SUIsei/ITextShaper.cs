namespace SUIsei;

/// <summary>
/// Turns a run of text into positioned glyphs against a <see cref="FontAtlas"/> (PlanningUI.md §D3 / MU5). The
/// default <see cref="SimpleTextShaper"/> is advance-based, one glyph per code point (no kerning or ligatures); a
/// real shaper (e.g. HarfBuzz) applies the font's <c>GSUB</c>/<c>GPOS</c> to produce ligatures, contextual forms,
/// and kerning. A <see cref="FontAtlas"/> owns the active shaper, so swapping it changes layout everywhere
/// (measure, the compositor's glyph runs, caret math) at once.
/// </summary>
public interface ITextShaper
{
	/// <summary>
	/// Lay <paramref name="text"/> out into positioned glyphs (EM units, baseline at the origin), appended to
	/// <paramref name="output"/>, and return the run's total advance width in EM. Zero-geometry glyphs (whitespace)
	/// advance the pen but are not appended.
	/// </summary>
	float Shape(ReadOnlySpan<char> text, FontAtlas font, List<ShapedGlyph> output);

	/// <summary>The total advance width of <paramref name="text"/> in EM — the width-only fast path for measurement,
	/// equal to the return of <see cref="Shape"/> but without producing the glyph list.</summary>
	float Measure(ReadOnlySpan<char> text, FontAtlas font);
}
