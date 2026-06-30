namespace SUIsei;

/// <summary>
/// The default <see cref="ITextShaper"/>: advance-based, left-to-right, one glyph per code point — no kerning,
/// ligatures, or contextual forms. Each glyph's cluster is its character index. This is the glyph layout
/// a font with no installed shaper uses.
/// </summary>
public sealed class SimpleTextShaper : ITextShaper
{
	public float Measure(ReadOnlySpan<char> text, IGlyphSource source)
	{
		var width = 0f;
		foreach (var c in text)
			width += source.Advance(c);
		return width;
	}

	public float Shape(ReadOnlySpan<char> text, IGlyphSource source, List<ShapedGlyph> output)
	{
		var pen = 0f;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			if (source.TryGetGlyph(c, out var g) && g.HasGeometry)
				output.Add(new ShapedGlyph(g, pen, 0f, 0f, i));
			pen += source.Advance(c);
		}
		return pen;
	}
}
