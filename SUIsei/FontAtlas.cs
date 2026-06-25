using System.Reflection;
using System.Text.Json;
using StbImageSharp;
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// A baked MTSDF font atlas: the decoded atlas page plus per-glyph layout, loaded from the pair an MSDF generator
/// emits — a PNG page (RGBA8 MTSDF) and a JSON layout. The page is decoded linear/UNORM, no mips: MTSDF texels are
/// signed distances, never sRGB colour. Plane bounds are EM units in a y-down, baseline-at-origin frame.
/// </summary>
public sealed class FontAtlas : IGlyphSource
{
	private readonly Dictionary<int, Glyph> _glyphs;          // keyed by Unicode code point (the cmap fast path)
	private readonly Dictionary<int, Glyph> _glyphsById;      // keyed by font glyph id (empty for a codepoint-only atlas)
	private readonly Dictionary<int, int> _codepointToGid;    // code point → glyph id (when ids were baked)

	private FontAtlas(AtlasImage image, int width, int height, float distanceRange, float bakeSize,
		float lineHeight, float ascender, float descender, Dictionary<int, Glyph> glyphs,
		Dictionary<int, Glyph> glyphsById, Dictionary<int, int> codepointToGid)
	{
		Image = image;
		Width = width;
		Height = height;
		DistanceRange = distanceRange;
		BakeSize = bakeSize;
		LineHeight = lineHeight;
		Ascender = ascender;
		Descender = descender;
		_glyphs = glyphs;
		_glyphsById = glyphsById;
		_codepointToGid = codepointToGid;
	}

	/// <summary>The decoded MTSDF page as neutral RGBA8 pixels — hand to the engine glue to upload as a GPU texture.</summary>
	public AtlasImage Image { get; }

	/// <summary>Atlas page dimensions in texels.</summary>
	public int Width { get; }
	public int Height { get; }

	/// <summary>The SDF distance range baked into the atlas, in atlas texels.</summary>
	public float DistanceRange { get; }

	/// <summary>The EM size the atlas was rasterized at, in texels.</summary>
	public float BakeSize { get; }

	/// <summary>Line advance in EM units (multiply by the on-screen font px size for the pixel line height).</summary>
	public float LineHeight { get; }

	/// <summary>Ascender / descender in EM units (y-down: ascender is above baseline, so it is negative).</summary>
	public float Ascender { get; }
	public float Descender { get; }

	/// <summary>Look up a glyph by Unicode code point. Returns false for code points outside the baked charset.</summary>
	public bool TryGetGlyph(int codepoint, out Glyph glyph) => _glyphs.TryGetValue(codepoint, out glyph);

	/// <summary>True when the atlas was baked with glyph ids, so it can be addressed by a shaper that emits ids
	/// (e.g. HarfBuzz). A codepoint-only atlas returns false and supports just <see cref="TryGetGlyph"/>.</summary>
	public bool HasGlyphIds => _glyphsById.Count > 0;

	/// <summary>Look up a glyph by font glyph id — the key a shaper addresses (including ligature/contextual glyphs
	/// that have no code point). Only populated when <see cref="HasGlyphIds"/>.</summary>
	public bool TryGetGlyphById(int glyphId, out Glyph glyph) => _glyphsById.TryGetValue(glyphId, out glyph);

	/// <summary>Map a Unicode code point to its font glyph id (the cmap), when ids were baked into the atlas.</summary>
	public bool TryGetGlyphId(int codepoint, out int glyphId) => _codepointToGid.TryGetValue(codepoint, out glyphId);

	/// <summary>
	/// The MTSDF distance range in screen pixels for a glyph run drawn at <paramref name="fontSizePx"/>.
	/// Fed to the shader as the smoothstep scale; scales with the on-screen size vs the baked EM size.
	/// </summary>
	public float ScreenPxRange(float fontSizePx) => DistanceRange * fontSizePx / BakeSize;

	/// <summary>The shaper that turns text into positioned glyphs for this atlas (PlanningUI.md §D3 / MU5). Defaults
	/// to the advance-based <see cref="SimpleTextShaper"/>; swap it (e.g. for a HarfBuzz shaper) to enable ligatures,
	/// contextual forms, and kerning across measurement, the compositor, and caret math at once.</summary>
	public ITextShaper Shaper { get; set; } = new SimpleTextShaper();

	/// <summary>Advance width of a code point in EM (falls back to the space glyph's advance for unknown code points).</summary>
	public float Advance(int codepoint)
	{
		if (_glyphs.TryGetValue(codepoint, out var g))
			return g.Advance;
		return _glyphs.TryGetValue(' ', out var space) ? space.Advance : 0.5f;
	}

	/// <summary>Measure a single-line string's advance width in EM units, through the atlas's <see cref="Shaper"/>.</summary>
	public float MeasureWidth(ReadOnlySpan<char> text) => Shaper.Measure(text, this);

	/// <summary>Decode an atlas from its JSON layout + PNG page streams.</summary>
	public static FontAtlas Load(Stream json, Stream png)
	{
		// Decode the PNG page to raw RGBA8 bytes using StbImageSharp (no engine dependency).
		var imageResult = ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);
		var atlasImage = new AtlasImage(imageResult.Width, imageResult.Height, imageResult.Data);

		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		var atlas = root.GetProperty("atlas");
		var distanceRange = atlas.GetProperty("distanceRange").GetSingle();
		var bakeSize = atlas.GetProperty("size").GetSingle();
		var width = atlas.GetProperty("width").GetInt32();
		var height = atlas.GetProperty("height").GetInt32();

		var metrics = root.GetProperty("metrics");
		var lineHeight = metrics.GetProperty("lineHeight").GetSingle();
		var ascender = metrics.GetProperty("ascender").GetSingle();
		var descender = metrics.GetProperty("descender").GetSingle();

		// A glyph entry carries a "unicode" (code point), a "glyphId" (font glyph index), or both. A codepoint-keyed
		// atlas has only "unicode"; a glyph-id-keyed atlas (for a shaper) has "glyphId" and, where one exists, also the
		// source "unicode". We index each glyph into whichever maps it has a key for, so both lookup paths stay valid.
		var glyphs = new Dictionary<int, Glyph>();
		var glyphsById = new Dictionary<int, Glyph>();
		var codepointToGid = new Dictionary<int, int>();
		foreach (var g in root.GetProperty("glyphs").EnumerateArray())
		{
			var cp = g.TryGetProperty("unicode", out var u) ? u.GetInt32() : -1;
			var gid = g.TryGetProperty("glyphId", out var gi) ? gi.GetInt32() : -1;
			var advance = g.GetProperty("advance").GetSingle();

			Glyph glyph;
			if (g.TryGetProperty("planeBounds", out var pb) && g.TryGetProperty("atlasBounds", out var ab))
			{
				glyph = new Glyph(
					cp, gid, advance, HasGeometry: true,
					pb.GetProperty("left").GetSingle(), pb.GetProperty("top").GetSingle(),
					pb.GetProperty("right").GetSingle(), pb.GetProperty("bottom").GetSingle(),
					ab.GetProperty("left").GetSingle() / width, ab.GetProperty("top").GetSingle() / height,
					ab.GetProperty("right").GetSingle() / width, ab.GetProperty("bottom").GetSingle() / height);
			}
			else
				glyph = new Glyph(cp, gid, advance, HasGeometry: false, 0, 0, 0, 0, 0, 0, 0, 0);

			if (cp >= 0)
				glyphs[cp] = glyph;
			if (gid >= 0)
			{
				glyphsById[gid] = glyph;
				if (cp >= 0)
					codepointToGid[cp] = gid;
			}
		}

		return new FontAtlas(atlasImage, width, height, distanceRange, bakeSize, lineHeight, ascender, descender,
			glyphs, glyphsById, codepointToGid);
	}

	/// <summary>
	/// The engine's single default font — the embedded ui-default (Cascadia Code MTSDF), loaded once on first use.
	/// </summary>
	public static FontAtlas Default => field ??= LoadEmbeddedDefault();

	/// <summary>Load the engine's embedded default atlas (ui-default, Cascadia Code MTSDF) from this assembly.</summary>
	public static FontAtlas LoadEmbeddedDefault()
	{
		var asm = typeof(FontAtlas).Assembly;
		using var json = OpenResource(asm, "ui-default.font.json");
		using var png = OpenResource(asm, "ui-default.0.png");
		return Load(json, png);
	}

	private static Stream OpenResource(Assembly asm, string logicalName) =>
		asm.GetManifestResourceStream(logicalName)
		?? throw new InvalidOperationException($"Embedded font resource '{logicalName}' not found in {asm.GetName().Name}.");
}
