using System.Text;
using System.Text.Json;

namespace BinaryVibrance.Vysara.Text.Atlas;

/// <summary>
/// Emits the <c>&lt;name&gt;.font.json</c> layout in the exact shape <see cref="BinaryVibrance.Vysara.FontAtlas"/> loads: an
/// <c>atlas</c> block (mtsdf, distanceRange, size, page dims), a <c>metrics</c> block (EM line metrics from the
/// OS/2 typo fields), and a <c>glyphs</c> array (unicode, advance, and plane/atlas bounds for non-blank glyphs).
/// </summary>
internal static class FontJsonWriter
{
	public static string Write(Fontset fontset, int width, int height, int unitsPerEm,
		int typoAscender, int typoDescender, int typoLineGap, IReadOnlyList<GlyphWork> glyphs)
	{
		var lineHeight = (typoAscender - typoDescender + typoLineGap) / (double)unitsPerEm;
		var ascender = -typoAscender / (double)unitsPerEm;
		var descender = -typoDescender / (double)unitsPerEm;

		using var stream = new MemoryStream();
		using (var w = new Utf8JsonWriter(stream))
		{
			w.WriteStartObject();

			w.WriteStartObject("atlas");
			w.WriteString("type", "mtsdf");
			w.WriteNumber("distanceRange", fontset.PxRange);
			w.WriteNumber("distanceRangeMiddle", 0);
			w.WriteNumber("size", fontset.GlyphSize);
			w.WriteNumber("width", width);
			w.WriteNumber("height", height);
			w.WriteString("yOrigin", "top");
			w.WriteEndObject();

			w.WriteStartObject("metrics");
			w.WriteNumber("emSize", 1);
			w.WriteNumber("lineHeight", lineHeight);
			w.WriteNumber("ascender", ascender);
			w.WriteNumber("descender", descender);
			w.WriteNumber("underlineY", 0);
			w.WriteNumber("underlineThickness", 0);
			w.WriteEndObject();

			// Code-point-keyed (default): order + key by "unicode". Glyph-id-keyed: order + key by "glyphId" and emit
			// "unicode" only where the glyph has a source code point (a ligature/contextual glyph has none).
			var ordered = fontset.GlyphIdKeyed ? glyphs.OrderBy(g => g.GlyphId) : glyphs.OrderBy(g => g.Codepoint);
			w.WriteStartArray("glyphs");
			foreach (var g in ordered)
			{
				w.WriteStartObject();
				if (fontset.GlyphIdKeyed)
				{
					w.WriteNumber("glyphId", g.GlyphId);
					if (g.Codepoint >= 0)
						w.WriteNumber("unicode", g.Codepoint);
				}
				else
					w.WriteNumber("unicode", g.Codepoint);
				w.WriteNumber("advance", g.Advance);
				if (g.HasGeometry)
				{
					w.WriteStartObject("planeBounds");
					w.WriteNumber("left", g.PlaneLeft);
					w.WriteNumber("top", g.PlaneTop);
					w.WriteNumber("right", g.PlaneRight);
					w.WriteNumber("bottom", g.PlaneBottom);
					w.WriteEndObject();

					w.WriteStartObject("atlasBounds");
					w.WriteNumber("left", g.AtlasX);
					w.WriteNumber("top", g.AtlasY);
					w.WriteNumber("right", g.AtlasX + g.Width);
					w.WriteNumber("bottom", g.AtlasY + g.Height);
					w.WriteEndObject();
				}

				w.WriteEndObject();
			}

			w.WriteEndArray();
			w.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}
}
