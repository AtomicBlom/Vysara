namespace SUIsei.Tests;

/// <summary>
/// The text-shaping seam (PlanningUI.md §D3 / MU5): a <see cref="FontAtlas"/> measures + lays out through its
/// <see cref="FontAtlas.Shaper"/>. The default is the advance-based <see cref="SimpleTextShaper"/>; installing a
/// different <see cref="ITextShaper"/> changes layout everywhere that goes through the atlas.
/// </summary>
public sealed class TextShaperTests
{
	[Test]
	public void Default_shaper_is_advance_based()
	{
		var font = FontAtlas.Default;

		font.Shaper.ShouldBeOfType<SimpleTextShaper>();
		// MeasureWidth routes through the shaper, which (by default) sums per-code-point advances.
		font.MeasureWidth("ABC").ShouldBe(font.Advance('A') + font.Advance('B') + font.Advance('C'), 1e-4f);
	}

	[Test]
	public void Default_shaper_emits_one_positioned_glyph_per_visible_code_point()
	{
		var font = FontAtlas.Default;
		var output = new List<ShapedGlyph>();

		var width = font.Shaper.Shape("A B", font, output);

		output.Count.ShouldBe(2);          // 'A' and 'B' have geometry; the space does not
		output[0].Cluster.ShouldBe(0);
		output[1].Cluster.ShouldBe(2);     // 'B' is the third character
		output[1].PenXEm.ShouldBe(font.Advance('A') + font.Advance(' '), 1e-4f);
		width.ShouldBe(font.MeasureWidth("A B"), 1e-4f);
	}

	[Test]
	public void Installing_a_shaper_changes_measurement_through_the_atlas()
	{
		// A standalone atlas so the swap does not leak into FontAtlas.Default (shared across tests).
		using var json = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.font.json")!;
		using var png = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.0.png")!;
		var font = FontAtlas.Load(json, png);

		font.Shaper = new FixedWidthShaper(width: 3.5f);

		font.MeasureWidth("anything at all").ShouldBe(3.5f);
	}

	[Test]
	public void Shaper_resolves_against_any_glyph_source_not_just_the_atlas()
	{
		// The shaper is written against IGlyphSource, so it lays out over a stub source with no FontAtlas involved —
		// the decoupling that lets the shaping backend and the imagery backend swap independently.
		var source = new StubGlyphSource();
		var output = new List<ShapedGlyph>();

		var width = new SimpleTextShaper().Shape("AB", source, output);

		output.Count.ShouldBe(2);
		output[0].PenXEm.ShouldBe(0f);
		output[1].PenXEm.ShouldBe(0.5f);   // pen advanced by 'A'
		width.ShouldBe(1.0f);              // 0.5 + 0.5
	}

	// A trivial shaper proving the seam is honored: it reports a constant width and produces no glyphs.
	private sealed class FixedWidthShaper(float width) : ITextShaper
	{
		public float Measure(ReadOnlySpan<char> text, IGlyphSource source) => width;

		public float Shape(ReadOnlySpan<char> text, IGlyphSource source, List<ShapedGlyph> output) => width;
	}

	// A minimal non-atlas glyph source: 'A'/'B' each advance 0.5 EM and have geometry; nothing else resolves.
	private sealed class StubGlyphSource : IGlyphSource
	{
		public bool TryGetGlyph(int codepoint, out Glyph glyph)
		{
			if (codepoint is 'A' or 'B')
			{
				glyph = new Glyph(codepoint, -1, 0.5f, HasGeometry: true, 0f, -0.7f, 0.5f, 0f, 0f, 0f, 1f, 1f);
				return true;
			}
			glyph = default;
			return false;
		}

		public bool TryGetGlyphById(int glyphId, out Glyph glyph) { glyph = default; return false; }
		public bool TryGetGlyphId(int codepoint, out int glyphId) { glyphId = -1; return false; }
		public bool HasGlyphIds => false;
		public float Advance(int codepoint) => 0.5f;
		public float Ascender => -0.9f;
		public float LineHeight => 1.2f;
	}
}
