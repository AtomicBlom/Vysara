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

	// A trivial shaper proving the seam is honored: it reports a constant width and produces no glyphs.
	private sealed class FixedWidthShaper(float width) : ITextShaper
	{
		public float Measure(ReadOnlySpan<char> text, FontAtlas font) => width;

		public float Shape(ReadOnlySpan<char> text, FontAtlas font, List<ShapedGlyph> output) => width;
	}
}
