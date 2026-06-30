
namespace SUIsei.Tests;

/// <summary>
/// Decode coverage for the baked MTSDF <see cref="FontAtlas"/>. Runs against the
/// engine's embedded default atlas (Cascadia Code) — a committed, hermetic fixture that ships inside SUIsei, so
/// the test needs no backend and no loose files. Pins the page format (linear/UNORM, no mips), the glyph-table
/// lookup, and the EM-unit metrics the compositor lays out against.
/// </summary>
public sealed class FontAtlasTests
{
	[Test]
	public void Embedded_default_decodes_with_a_linear_unmipped_page()
	{
		var font = FontAtlas.LoadEmbeddedDefault();

		font.Width.ShouldBeGreaterThan(0);
		font.Height.ShouldBeGreaterThan(0);
		// MTSDF texels are signed distances, not colour: the decoded page is a single un-mipped RGBA8 image
		// (4 bytes/texel) sized to the atlas — never mipped, since filtering would ruin the SDF.
		font.Image.Width.ShouldBe(font.Width);
		font.Image.Height.ShouldBe(font.Height);
		font.Image.Pixels.Length.ShouldBe(font.Width * font.Height * 4);
		font.DistanceRange.ShouldBeGreaterThan(0f);
		font.BakeSize.ShouldBeGreaterThan(0f);
	}

	[Test]
	public void A_letter_has_geometry_and_space_is_blank()
	{
		var font = FontAtlas.LoadEmbeddedDefault();

		font.TryGetGlyph('A', out var a).ShouldBeTrue();
		a.HasGeometry.ShouldBeTrue();
		a.Advance.ShouldBeGreaterThan(0f);
		// A real UV cell: left before right, and top above bottom (the atlas was baked yOrigin: top).
		(a.U1 > a.U0).ShouldBeTrue();
		(a.V1 > a.V0).ShouldBeTrue();

		font.TryGetGlyph(' ', out var space).ShouldBeTrue();
		space.HasGeometry.ShouldBeFalse(); // whitespace carries an advance but no quad/atlas cell
		space.Advance.ShouldBeGreaterThan(0f);
	}

	[Test]
	public void ScreenPxRange_scales_the_distance_range_with_font_size()
	{
		var font = FontAtlas.LoadEmbeddedDefault();
		// At the baked EM size the on-screen range equals the baked distance range; doubling the size doubles it.
		font.ScreenPxRange(font.BakeSize).ShouldBe(font.DistanceRange, 1e-3f);
		font.ScreenPxRange(font.BakeSize * 2f).ShouldBe(font.DistanceRange * 2f, 1e-3f);
	}

	[Test]
	public void MeasureWidth_sums_glyph_advances()
	{
		var font = FontAtlas.LoadEmbeddedDefault();
		var expected = font.Advance('A') + font.Advance('B') + font.Advance('C');
		font.MeasureWidth("ABC").ShouldBe(expected, 1e-4f);
	}
}
