using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Rasterizer;
using BinaryVibrance.Vysara.Text.OpenType;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// A glyph run's outline and shadow, drawn from the atlas's true-distance channel by the Glyph prim: <c>Custom</c> =
/// (screenPxRange, bias, softness, 0), bias and softness in units of the distance range, both 0 for the glyphs
/// themselves. Every glyph's shadow is emitted before every glyph's outline, and those before the glyphs, so no effect
/// lands on a neighbouring glyph. Probed through the CPU rasterizer, the reference the GPU's Glyph arm matches.
/// </summary>
public sealed class GlyphEffectsTests
{
	private static readonly CompositionColorBrush White = new(HdrColor.FromRgb(255, 255, 255));
	private static readonly CompositionColorBrush Red = new(HdrColor.FromRgb(255, 0, 0));

	private static DrawListData Composite(GlyphRunContent run, Vector2 at, int w, int h, float opacity = 1f)
	{
		var root = new RootVisual { SurfaceSize = new Vector2(w, h) };
		root.Add(new SpriteVisual { Transform = Matrix3x2.CreateTranslation(at), Content = run, Opacity = opacity });
		var compositor = new DrawListCompositor();
		compositor.Composite(root, new DrawListId(0));
		return compositor.LastDraw(new DrawListId(0));
	}

	private static SoftwareRasterizer Rasterize(GlyphRunContent run, Vector2 at, int w, int h, float opacity = 1f)
	{
		var rasterizer = new SoftwareRasterizer(w, h);
		rasterizer.Render(Composite(run, at, w, h, opacity));
		return rasterizer;
	}

	// The left edge of a '|' in Cascadia Code, in EM from the pen: a straight vertical edge the true distance holds exactly.
	private static float BarLeftEm()
	{
		var font = OpenTypeFont.Load(MsdfGenerationTests.CascadiaCode);
		return (float)(font.GetOutline(font.GetGlyphIndex('|')).ComputeBounds().Left / font.UnitsPerEm);
	}

	// A row through the middle of a '|' at `fontSize`, `top` pixels down.
	private static int BarRow(float fontSize, float top) => (int)(top + (-FontAtlas.Default.Ascender - 0.35f) * fontSize);

	[Test]
	public void A_run_emits_every_shadow_then_every_outline_then_the_glyphs_in_one_draw()
	{
		var atlas = FontAtlas.Default;
		var run = new GlyphRunContent("ab", 32f, White);
		run.UpdateEffects(0.1f, Red, new Vector2(0.05f, 0.1f), 0.2f, Red);

		var draw = Composite(run, Vector2.Zero, 200, 60);
		draw.Commands.Length.ShouldBe(1);
		var custom = draw.Vertices.ToArray().Select(v => v.Custom).ToArray();
		custom.Length.ShouldBe(3 * 2 * 4);

		var range = atlas.ScreenPxRange(32f);
		var shadow = new Vector4(range, 0f, 0.2f / atlas.DistanceRangeEm, 0f);
		var outline = new Vector4(range, 0.1f / atlas.DistanceRangeEm, 0f, 0f);
		var fill = new Vector4(range, 0f, 0f, 0f);
		for (var i = 0; i < custom.Length; i++)
		{
			var expected = i < 8 ? shadow : i < 16 ? outline : fill;
			Vector4.Distance(custom[i], expected).ShouldBeLessThan(1e-5f, $"vertex {i}");
		}

		// The shadow's quads are the glyphs' moved by its offset: 0.05 EM across and 0.1 EM down at 32.
		var positions = draw.Vertices.ToArray().Select(v => v.Position).ToArray();
		(positions[0] - positions[16]).X.ShouldBe(1.6f, 1e-4f);
		(positions[0] - positions[16]).Y.ShouldBe(3.2f, 1e-4f);
		(positions[8] - positions[16]).ShouldBe(Vector2.Zero);
	}

	[Test]
	public void A_run_without_effects_draws_only_its_glyphs()
	{
		var draw = Composite(new GlyphRunContent("ab", 32f, White), Vector2.Zero, 200, 60);
		draw.Vertices.Length.ShouldBe(2 * 4);
		draw.Vertices.ToArray().ShouldAllBe(v => v.Custom.Y == 0f && v.Custom.Z == 0f);
	}

	[Test]
	public void An_outline_grows_the_glyphs_by_its_thickness_with_a_one_pixel_ramp()
	{
		// A red outline 0.1 EM thick (4 px at 40) round a white '|', on nothing. Outside the ink the fill draws nothing, so
		// coverage is the outline's: full and exactly red out to the thickness, then a one-pixel ramp centred on it.
		const float fontSize = 40f;
		const float thickness = 0.1f;
		var at = new Vector2(20f, 4f);
		var run = new GlyphRunContent("|", fontSize, White);
		run.UpdateEffects(thickness, Red, default, 0f, null);
		var rasterizer = Rasterize(run, at, 80, 60);

		var edge = at.X + BarLeftEm() * fontSize;
		var reach = thickness * fontSize;
		var row = BarRow(fontSize, at.Y);
		var probed = 0;
		for (var x = (int)(edge - reach) - 3; x < (int)edge - 1; x++)
		{
			var outside = edge - (x + 0.5f);
			if (outside < 0.6f)
				continue;
			var px = rasterizer.ReadLinear(x, row);
			px.Y.ShouldBe(0f, $"no fill at {outside:0.##} px out");
			px.W.ShouldBe(Math.Clamp(reach + 0.5f - outside, 0f, 1f), 0.06f, $"outline coverage at {outside:0.##} px out");
			if (outside < reach - 0.6f)
				Vector4.Distance(px, new Vector4(1f, 0f, 0f, 1f)).ShouldBeLessThan(1e-5f, $"opaque outline at {outside:0.##} px out");
			probed++;
		}
		probed.ShouldBeGreaterThan(5);
	}

	[Test]
	public void A_soft_shadow_fades_across_its_softness_centred_on_the_moved_edge()
	{
		// A shadow of a '|' 0.5 EM to the right, its edge 0.25 EM soft (10 px at 40), no glyphs over it. Outside the moved
		// edge coverage falls linearly from a half to none across half the softness.
		const float fontSize = 40f;
		const float softness = 0.25f;
		var at = new Vector2(10f, 4f);
		var run = new GlyphRunContent("|", fontSize, null);
		run.UpdateEffects(0f, null, new Vector2(0.5f, 0f), softness, White);
		var rasterizer = Rasterize(run, at, 80, 60);

		var edge = at.X + (BarLeftEm() + 0.5f) * fontSize;
		var width = softness * fontSize;
		var row = BarRow(fontSize, at.Y);
		var probed = 0;
		for (var x = (int)(edge - width / 2f) - 1; x <= (int)edge; x++)
		{
			var outside = edge - (x + 0.5f);
			if (outside < 0f)
				continue;
			rasterizer.ReadLinear(x, row).W.ShouldBe(Math.Clamp(0.5f - outside / width, 0f, 1f), 0.03f, $"shadow at {outside:0.##} px out");
			probed++;
		}
		probed.ShouldBeGreaterThan(3);
	}

	[Test]
	public void A_hard_shadow_is_the_glyphs_moved_by_its_offset()
	{
		// With no softness the shadow reads the same median as the glyphs, so a shadow moved by whole pixels is the glyphs'
		// own pixels moved by them.
		const float fontSize = 32f;
		var glyphs = Rasterize(new GlyphRunContent("Ag$", fontSize, White), new Vector2(4f, 4f), 120, 60);
		var run = new GlyphRunContent("Ag$", fontSize, null);
		run.UpdateEffects(0f, null, new Vector2(0.25f, 0.125f), 0f, White);
		var shadow = Rasterize(run, new Vector2(4f, 4f), 120, 60);

		var inked = 0;
		for (var y = 0; y < 52; y++)
			for (var x = 0; x < 112; x++)
			{
				var expected = glyphs.ReadLinear(x, y);
				Vector4.Distance(shadow.ReadLinear(x + 8, y + 4), expected).ShouldBeLessThan(1e-4f, $"({x},{y})");
				if (expected.W > 0.5f)
					inked++;
			}
		inked.ShouldBeGreaterThan(100);
	}

	[Test]
	public void An_outline_lies_under_every_glyph_so_a_neighbours_outline_never_covers_ink()
	{
		// Two Ms whose outlines, 0.15 EM thick, reach into each other's ink. Wherever the glyphs alone are fully covered,
		// the outlined run is exactly the glyphs.
		const float fontSize = 40f;
		var at = new Vector2(10f, 4f);
		var glyphs = Rasterize(new GlyphRunContent("MM", fontSize, White), at, 80, 60);
		var run = new GlyphRunContent("MM", fontSize, White);
		run.UpdateEffects(0.15f, Red, default, 0f, null);
		var outlined = Rasterize(run, at, 80, 60);

		var inside = 0;
		for (var y = 0; y < 60; y++)
			for (var x = 0; x < 80; x++)
			{
				if (glyphs.ReadLinear(x, y).W < 1f)
					continue;
				Vector4.Distance(outlined.ReadLinear(x, y), glyphs.ReadLinear(x, y)).ShouldBeLessThan(1e-5f, $"ink at ({x},{y})");
				inside++;
			}
		inside.ShouldBeGreaterThan(100);
	}

	[Test]
	public void An_outline_at_the_fields_reach_keeps_its_ramp_inside_the_field()
	{
		// At the field's whole reach the outline is drawn half a pixel short of it, so its ramp ends inside the field: the
		// corners of an 'o''s quad, beyond the field, stay empty rather than reading half covered.
		const float fontSize = 40f;
		var atlas = FontAtlas.Default;
		var run = new GlyphRunContent("o", fontSize, null);
		run.UpdateEffects(atlas.DistanceRangeEm / 2f, Red, default, 0f, null);
		var at = new Vector2(10f, 4f);
		var draw = Composite(run, at, 60, 60);
		var range = atlas.ScreenPxRange(fontSize);
		draw.Vertices.Span[0].Custom.Y.ShouldBe(0.5f - 0.5f / range, 1e-5f);

		var rasterizer = new SoftwareRasterizer(60, 60);
		rasterizer.Render(draw);
		var topLeft = draw.Vertices.Span[0].Position;
		var bottomRight = draw.Vertices.Span[2].Position;
		foreach (var (x, y) in new[] { (topLeft.X + 1f, topLeft.Y + 1f), (bottomRight.X - 1f, topLeft.Y + 1f),
			(topLeft.X + 1f, bottomRight.Y - 1f), (bottomRight.X - 1f, bottomRight.Y - 1f) })
			rasterizer.ReadLinear((int)x, (int)y).W.ShouldBeLessThan(1e-4f, $"corner ({x:0},{y:0})");
		// The outline itself is drawn: the middle of the 'o''s left side is covered.
		rasterizer.ReadLinear((int)(topLeft.X + range * 0.5f), (int)((topLeft.Y + bottomRight.Y) / 2f)).W.ShouldBe(1f);
	}

	[Test]
	public void A_faded_run_with_effects_fades_as_one_layer()
	{
		// A white run over a red outline at half opacity: inside the glyphs only the faded white shows, no outline through
		// it, as a WinUI element fades as a whole.
		const float fontSize = 40f;
		var at = new Vector2(10f, 4f);
		var run = new GlyphRunContent("|", fontSize, White);
		run.UpdateEffects(0.1f, Red, default, 0f, null);
		var rasterizer = new SoftwareRasterizer(80, 60);
		rasterizer.Render(Composite(run, at, 80, 60, opacity: 0.5f));
		rasterizer.LayersComposited.ShouldBe(1);

		var font = OpenTypeFont.Load(MsdfGenerationTests.CascadiaCode);
		var bar = font.GetOutline(font.GetGlyphIndex('|')).ComputeBounds();
		var middle = at.X + (float)((bar.Left + bar.Right) / 2 / font.UnitsPerEm) * fontSize;
		var px = rasterizer.ReadLinear((int)middle, BarRow(fontSize, at.Y));
		px.X.ShouldBe(0.5f, 1e-3f);
		px.Y.ShouldBe(0.5f, 1e-3f);

		// Without effects a faded run needs no layer.
		var plain = new SoftwareRasterizer(80, 60);
		plain.Render(Composite(new GlyphRunContent("|", fontSize, White), at, 80, 60, opacity: 0.5f));
		plain.LayersComposited.ShouldBe(0);
	}

	[Test]
	public void A_run_dumps_its_effects()
	{
		var run = new GlyphRunContent("Hi", 16f, White);
		run.UpdateEffects(0.08f, Red, new Vector2(0.05f, 0.1f), 0.2f, new CompositionColorBrush(HdrColor.FromRgb(0, 0, 0)) { Opacity = 0.5f });
		new SpriteVisual { Content = run }.Dump().ShouldContain(
			" glyphs=\"Hi\" size=16 brush=(1,1,1,1) outline=0.08(1,0,0,1) shadow=0.05,0.1(0,0,0,1)*0.5 softness=0.2\n");
	}
}
