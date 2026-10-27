using System.Runtime.InteropServices;

using HarfBuzzSharp;

using Buffer = HarfBuzzSharp.Buffer;

namespace SUIsei.HarfBuzz;

/// <summary>
/// An <see cref="ITextShaper"/> backed by HarfBuzz: it shapes a run with the font's <c>GSUB</c>/<c>GPOS</c>
/// (ligatures, contextual forms, kerning) into glyph ids + positions, then resolves each glyph id against the
/// supplied <see cref="IGlyphSource"/> — so it must be paired with a glyph-id-keyed atlas
/// (<see cref="IGlyphSource.HasGlyphIds"/>). Construct it from the same font file the atlas was baked from. Install
/// it with <c>fontAtlas.Shaper = new HarfBuzzTextShaper(fontBytes)</c>. A run's <see cref="FontFeatureSet"/> reaches
/// HarfBuzz as features over the whole run (<c>tnum</c>, <c>calt=0</c>), on top of the font's defaults; the atlas must
/// hold the glyphs they substitute, which a glyph-id atlas baked with GSUB-reachable glyphs does.
/// </summary>
public sealed class HarfBuzzTextShaper : ITextShaper, IDisposable
{
	private readonly Blob _blob;
	private readonly Face _face;
	private readonly Font _font;
	private readonly float _unitsPerEm;
	private readonly Dictionary<FontFeatureSet, Feature[]> _features = [];

	public HarfBuzzTextShaper(byte[] fontData)
	{
		// MemoryMode.Duplicate makes HarfBuzz copy the bytes, so the managed array need not stay pinned afterwards.
		var handle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
		try
		{
			_blob = new Blob(handle.AddrOfPinnedObject(), fontData.Length, MemoryMode.Duplicate);
		}
		finally
		{
			handle.Free();
		}

		_face = new Face(_blob, 0);
		_unitsPerEm = _face.UnitsPerEm;
		_font = new Font(_face);
		// Scale in font units, so advances/offsets come back in font units and divide cleanly to EM.
		_font.SetScale(_face.UnitsPerEm, _face.UnitsPerEm);
	}

	public float Measure(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features)
	{
		if (text.IsEmpty)
			return 0f;
		using var buffer = ShapeRun(text, features);
		var width = 0;
		foreach (var p in buffer.GlyphPositions)
			width += p.XAdvance;
		return width / _unitsPerEm;
	}

	public float Shape(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features, List<ShapedGlyph> output)
	{
		if (text.IsEmpty)
			return 0f;
		using var buffer = ShapeRun(text, features);
		var infos = buffer.GlyphInfos;
		var positions = buffer.GlyphPositions;

		var pen = 0f;
		for (var i = 0; i < infos.Length; i++)
		{
			var glyphId = (int)infos[i].Codepoint; // after shaping, Codepoint holds the glyph id
			var xOffset = positions[i].XOffset / _unitsPerEm;
			var yOffset = -positions[i].YOffset / _unitsPerEm; // HarfBuzz is y-up; the atlas plane frame is y-down
			if (source.TryGetGlyphById(glyphId, out var glyph) && glyph.HasGeometry)
				output.Add(new ShapedGlyph(glyph, pen, xOffset, yOffset, (int)infos[i].Cluster));
			pen += positions[i].XAdvance / _unitsPerEm;
		}
		return pen;
	}

	private Buffer ShapeRun(ReadOnlySpan<char> text, FontFeatureSet features)
	{
		var buffer = new Buffer();
		buffer.AddUtf16(text.ToString());
		buffer.GuessSegmentProperties(); // direction / script / language from the run's content
		_font.Shape(buffer, FeaturesFor(features));
		return buffer;
	}

	// HarfBuzz's form of a feature set, each over the whole run, converted once per set the shaper sees.
	private Feature[] FeaturesFor(FontFeatureSet features)
	{
		if (features.IsEmpty)
			return [];
		if (_features.TryGetValue(features, out var converted))
			return converted;
		converted = new Feature[features.Features.Length];
		for (var i = 0; i < converted.Length; i++)
			converted[i] = Feature.Parse(features.Features[i].ToString());
		_features[features] = converted;
		return converted;
	}

	public void Dispose()
	{
		_font.Dispose();
		_face.Dispose();
		_blob.Dispose();
	}
}
