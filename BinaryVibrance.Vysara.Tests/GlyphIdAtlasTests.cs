using System.Text;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The glyph-id-keyed atlas mode: a baked atlas may carry <c>glyphId</c> per entry (with
/// <c>unicode</c> where one exists) so a shaper can address glyphs — including ligature/contextual glyphs that have
/// no code point — by id. A plain codepoint-keyed atlas stays the default and reports no ids.
/// </summary>
public sealed class GlyphIdAtlasTests
{
	// A minimal glyph-id atlas: 'A' carries both unicode + glyphId; a ligature glyph carries only a glyphId.
	private const string GlyphIdJson = """
		{
		  "atlas": { "type": "mtsdf", "distanceRange": 4, "size": 32, "width": 256, "height": 256, "yOrigin": "top" },
		  "metrics": { "emSize": 1, "lineHeight": 1.2, "ascender": -0.9, "descender": 0.2 },
		  "glyphs": [
		    { "unicode": 65, "glyphId": 36, "advance": 0.6,
		      "planeBounds": { "left": 0, "top": -0.7, "right": 0.6, "bottom": 0 },
		      "atlasBounds": { "left": 0, "top": 0, "right": 20, "bottom": 24 } },
		    { "glyphId": 1425, "advance": 0.6,
		      "planeBounds": { "left": 0, "top": -0.7, "right": 0.6, "bottom": 0 },
		      "atlasBounds": { "left": 20, "top": 0, "right": 40, "bottom": 24 } }
		  ]
		}
		""";

	private static FontAtlas LoadGlyphIdAtlas()
	{
		var json = new MemoryStream(Encoding.UTF8.GetBytes(GlyphIdJson));
		var png = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.0.png")!; // any valid page
		return FontAtlas.Load(json, png);
	}

	[Test]
	public void Codepoint_atlas_reports_no_glyph_ids()
	{
		FontAtlas.Default.HasGlyphIds.ShouldBeFalse();
	}

	[Test]
	public void Glyph_id_atlas_supports_both_lookups()
	{
		var font = LoadGlyphIdAtlas();

		font.HasGlyphIds.ShouldBeTrue();

		// 'A' resolves by code point and by id, and the cmap maps between them.
		font.TryGetGlyph('A', out var byCp).ShouldBeTrue();
		byCp.GlyphId.ShouldBe(36);
		font.TryGetGlyphId('A', out var gid).ShouldBeTrue();
		gid.ShouldBe(36);
		font.TryGetGlyphById(36, out var byId).ShouldBeTrue();
		byId.Codepoint.ShouldBe((int)'A');
	}

	[Test]
	public void Ligature_glyph_is_reachable_only_by_id()
	{
		var font = LoadGlyphIdAtlas();

		font.TryGetGlyphById(1425, out var lig).ShouldBeTrue();
		lig.Codepoint.ShouldBe(-1);                  // no source code point
		font.TryGetGlyph(1425, out _).ShouldBeFalse(); // not addressable as a code point
	}
}
