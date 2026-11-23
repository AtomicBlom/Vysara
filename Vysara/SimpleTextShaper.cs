namespace Vysara;

/// <summary>
/// The default <see cref="ITextShaper"/>: advance-based, left-to-right, one glyph per code point — no kerning,
/// ligatures, or contextual forms. Each glyph's cluster is its character index. This is the glyph layout
/// a font with no installed shaper uses.
/// <para>
/// Of the OpenType features it applies one, <c>tnum</c>, the one it can without the font's tables: every ASCII digit
/// advances by the widest digit's advance, centred in it, so columns of numbers line up. Others, <c>pnum</c> included,
/// leave the font's own advances, so a font whose digits are tabular by design stays tabular here.
/// </para>
/// </summary>
public sealed class SimpleTextShaper : ITextShaper
{
	private static readonly uint Tnum = FontFeature.TabularFigures.Tag;

	public float Measure(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features)
	{
		var tabular = TabularAdvance(source, features);
		var width = 0f;
		foreach (var c in text)
			width += tabular > 0 && char.IsAsciiDigit(c) ? tabular : source.Advance(c);
		return width;
	}

	public float Shape(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features, List<ShapedGlyph> output)
	{
		var tabular = TabularAdvance(source, features);
		var pen = 0f;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			var advance = source.Advance(c);
			var offset = 0f;
			if (tabular > 0 && char.IsAsciiDigit(c))
			{
				offset = (tabular - advance) / 2;
				advance = tabular;
			}

			if (source.TryGetGlyph(c, out var g) && g.HasGeometry)
				output.Add(new ShapedGlyph(g, pen, offset, 0f, i));
			pen += advance;
		}
		return pen;
	}

	// The advance every digit takes under tnum, the widest digit's; 0 when tnum is off.
	private static float TabularAdvance(IGlyphSource source, FontFeatureSet features)
	{
		if (!features.IsOn(Tnum))
			return 0f;
		var widest = 0f;
		for (var digit = '0'; digit <= '9'; digit++)
			widest = MathF.Max(widest, source.Advance(digit));
		return widest;
	}
}
