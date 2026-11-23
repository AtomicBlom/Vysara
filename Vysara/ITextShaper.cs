namespace Vysara;

/// <summary>
/// Turns a run of text into positioned glyphs against an <see cref="IGlyphSource"/> — it
/// is written against the source interface, never a concrete atlas, so the shaping backend and the imagery backend
/// stay independent. The default <see cref="SimpleTextShaper"/> is advance-based, one glyph per code point (no
/// kerning or ligatures); a real shaper (e.g. HarfBuzz) applies the font's <c>GSUB</c>/<c>GPOS</c> to produce
/// ligatures, contextual forms, and kerning, resolving the glyph ids it emits back through the source. A
/// <see cref="FontAtlas"/> owns the active shaper, so swapping it changes layout everywhere (measure, the
/// compositor's glyph runs, caret math) at once.
/// <para>
/// Both calls take the run's OpenType <see cref="FontFeatureSet"/> (<see cref="FontFeatureSet.Empty"/> for the
/// defaults). A shaper applies the features it can and ignores the rest; measuring and laying out with the same set
/// give the same width.
/// </para>
/// </summary>
public interface ITextShaper
{
	/// <summary>
	/// Lay <paramref name="text"/> out into positioned glyphs (EM units, baseline at the origin), resolving them
	/// against <paramref name="source"/> with <paramref name="features"/> applied, appended to
	/// <paramref name="output"/>, and return the run's total advance width in EM. Zero-geometry glyphs (whitespace)
	/// advance the pen but are not appended.
	/// </summary>
	float Shape(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features, List<ShapedGlyph> output);

	/// <summary>The total advance width of <paramref name="text"/> in EM with <paramref name="features"/> applied — the
	/// width-only fast path for measurement, equal to the return of <see cref="Shape"/> but without producing the
	/// glyph list.</summary>
	float Measure(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features);
}
