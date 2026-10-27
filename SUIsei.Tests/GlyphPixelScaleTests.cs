using System.Numerics;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Rasterizer;
using SUIsei.Text.OpenType;

namespace SUIsei.Tests;

/// <summary>
/// A glyph run's <c>screenPxRange</c> (the coverage scale in <see cref="OverlayVertex.Custom"/>.x) is the atlas's
/// distance range at the size the glyphs land at on the surface: the run's font size times the smallest stretch of
/// everything above it, the root's layout scale and any render transform. So the edge ramp is one surface pixel wide at
/// any display scale, as the analytic prims' ramps are.
/// </summary>
public sealed class GlyphPixelScaleTests
{
	private static DrawListData Render(UIElement content, float layoutScale, float w = 400f, float h = 100f)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content, LayoutScale = layoutScale };
		root.LayoutPass(new Size(w * layoutScale, h * layoutScale));
		return root.Render();
	}

	[Test]
	[Arguments(1f)]
	[Arguments(1.5f)]
	[Arguments(2f)]
	public void ScreenPxRange_follows_the_layout_scale(float layoutScale)
	{
		var draw = Render(new TextBlock { Text = "M", FontSize = 20f }, layoutScale);
		draw.Vertices.Span[0].Custom.X.ShouldBe(FontAtlas.Default.ScreenPxRange(20f * layoutScale), 1e-4f);
	}

	[Test]
	public void ScreenPxRange_follows_a_render_transform_by_its_smallest_stretch()
	{
		float RangeUnder(Transform transform) =>
			Render(new TextBlock { Text = "M", FontSize = 20f, RenderTransform = transform }, 1f).Vertices.Span[0].Custom.X;

		var atTwenty = FontAtlas.Default.ScreenPxRange(20f);
		RangeUnder(new ScaleTransform { ScaleX = 2f, ScaleY = 2f }).ShouldBe(atTwenty * 2f, 1e-4f);
		RangeUnder(new RotateTransform { Angle = 30f }).ShouldBe(atTwenty, 1e-3f);
		// Squeezed one way: the ramp is a pixel across the squeezed direction, so it never gets narrower than one.
		RangeUnder(new ScaleTransform { ScaleX = 3f, ScaleY = 0.5f }).ShouldBe(atTwenty * 0.5f, 1e-4f);
	}

	[Test]
	public void A_glyph_run_at_twice_the_layout_scale_dumps_its_surface_size_and_range()
	{
		// One '|' at 16 DIPs on a 2x surface: the quad in surface pixels, the range at 32 px, the font's own bake size.
		var dump = Render(new TextBlock { Text = "|", FontSize = 16f }, 2f, 20f, 24f).Dump();
		var range = FontAtlas.Default.ScreenPxRange(32f);
		range.ShouldBe(FontAtlas.Default.DistanceRange, 1e-4f);
		dump.ShouldContain("tag=3(Glyph) tex=1 clip=(0,0 40x48) idx=0+6 vtx=0");
		dump.ShouldContain($"custom=({range:0.###},0,0,0)");
		dump.Split('\n').Count(l => l.Contains($"custom=({range:0.###},0,0,0)")).ShouldBe(4);
	}

	[Test]
	[Arguments(1f)]
	[Arguments(1.5f)]
	[Arguments(2f)]
	public void Glyph_edges_ramp_over_one_surface_pixel_at_any_layout_scale(float layoutScale)
	{
		// A row of '|' bars, whose sides are straight vertical edges an MTSDF reproduces exactly. Each bar lands at its own
		// sub-pixel phase, so between them the pixel centres sample the whole ramp. Wherever a centre is within half a
		// pixel of an edge its coverage must be 0.5 plus its distance inside, in surface pixels: a ramp one pixel wide.
		const float fontSize = 24f;
		const int bars = 12;
		var font = OpenTypeFont.Load(MsdfGenerationTests.CascadiaCode);
		var bar = font.GetOutline(font.GetGlyphIndex('|')).ComputeBounds();
		var atlas = FontAtlas.Default;
		var advance = atlas.Advance('|');

		var width = (int)MathF.Ceiling(bars * advance * fontSize * layoutScale) + 2;
		var height = (int)MathF.Ceiling(atlas.LineHeight * fontSize * layoutScale) + 2;
		var draw = Render(new TextBlock { Text = new string('|', bars), FontSize = fontSize }, layoutScale, width / layoutScale, height / layoutScale);
		var rasterizer = new SoftwareRasterizer(width, height);
		rasterizer.Render(draw);

		// A row through the middle of the bars, between the baseline and the top of a capital.
		var row = (int)((-atlas.Ascender - 0.35f) * fontSize * layoutScale);
		var probed = 0;
		var worst = 0f;
		for (var k = 0; k < bars; k++)
		{
			foreach (var (edgeEm, inkRight) in new[] { (bar.Left / font.UnitsPerEm, true), (bar.Right / font.UnitsPerEm, false) })
			{
				var edge = (k * advance + (float)edgeEm) * fontSize * layoutScale;
				for (var x = (int)edge - 1; x <= (int)edge + 1; x++)
				{
					var inside = inkRight ? x + 0.5f - edge : edge - (x + 0.5f);
					if (MathF.Abs(inside) >= 0.45f)
						continue;
					var coverage = rasterizer.ReadLinear(x, row).W;
					worst = MathF.Max(worst, MathF.Abs(coverage - (0.5f + inside)));
					probed++;
				}
			}
		}

		probed.ShouldBeGreaterThan(bars);
		worst.ShouldBeLessThan(0.06f);
	}
}
