using System.Text;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Rasterizer;
using BinaryVibrance.Vysara.Text;
using BinaryVibrance.Vysara.Text.Atlas;
using BinaryVibrance.Vysara.Text.Geometry;
using BinaryVibrance.Vysara.Text.OpenType;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The native MTSDF generator (<c>BinaryVibrance.Vysara.Text</c>) against the font program it bakes. A field is right when a renderer
/// sampling it reconstructs the glyph's exact fill (the nonzero winding of its outline) everywhere outside the
/// antialiasing ramp, at texel centres and between them, where bilinear filtering mixes channels from different edges.
/// The committed default atlas is held to being exactly what baking its fontset produces.
/// </summary>
public sealed class MsdfGenerationTests
{
	internal static byte[] CascadiaCode => field ??= ReadResource("CascadiaCode.ttf");

	private static Fontset DefaultFontset => Fontset.Parse(Encoding.UTF8.GetString(ReadResource("ui-default.fontset.json")));

	// The default fontset baked afresh by this build's generator, once for every test that reads it.
	private static readonly Lazy<BakeResult> DefaultBake = new(() => MsdfBaker.Bake(DefaultFontset, _ => CascadiaCode));

	private static byte[] ReadResource(string name)
	{
		using var stream = typeof(MsdfGenerationTests).Assembly.GetManifestResourceStream(name)
			?? throw new InvalidOperationException($"Test resource '{name}' not found.");
		using var ms = new MemoryStream();
		stream.CopyTo(ms);
		return ms.ToArray();
	}

	// A square ring of quadratic edges: an outer square (counter-clockwise in y-up, filled) around a hole (clockwise),
	// each side split in two at its middle, so the vertices at (±outer, 0) and (±inner, 0) are shared by two edges that
	// both keep moving the same way in y.
	private static Shape SquareRing(Func<Vector2D, Vector2D, EdgeSegment> edge, double outer, double inner)
	{
		var shape = new Shape();
		shape.Contours.Add(Loop(edge, outer, clockwise: false));
		shape.Contours.Add(Loop(edge, inner, clockwise: true));
		return shape;

		static Contour Loop(Func<Vector2D, Vector2D, EdgeSegment> edge, double h, bool clockwise)
		{
			Vector2D[] corners = [new(h, -h), new(h, 0), new(h, h), new(0, h), new(-h, h), new(-h, 0), new(-h, -h), new(0, -h)];
			if (clockwise)
				Array.Reverse(corners);
			var contour = new Contour();
			for (var i = 0; i < corners.Length; i++)
				contour.Add(edge(corners[i], corners[(i + 1) % corners.Length]));
			return contour;
		}
	}

	private static EdgeSegment Quadratic(Vector2D a, Vector2D b) => new QuadraticSegment(a, Vector2D.Mix(a, b, 0.5), b);

	private static EdgeSegment Cubic(Vector2D a, Vector2D b) =>
		new CubicSegment(a, Vector2D.Mix(a, b, 1 / 3.0), Vector2D.Mix(a, b, 2 / 3.0), b);

	[Test]
	public void A_ray_through_a_vertex_two_curves_share_counts_it_once()
	{
		// The ray from the hole's centre runs through the hole's and the outer square's right-hand vertices, each the
		// end of one edge and the start of the next. Counted at both, the hole's -1 doubles and the hole reads filled.
		foreach (var edge in new Func<Vector2D, Vector2D, EdgeSegment>[] { Quadratic, Cubic })
		{
			var ring = SquareRing(edge, outer: 10, inner: 5);
			ring.WindingAt(new Vector2D(0, 0)).ShouldBe(0);         // in the hole
			Math.Abs(ring.WindingAt(new Vector2D(-7.5, 0))).ShouldBe(1); // in the ring, through the hole's left vertex
			ring.WindingAt(new Vector2D(-12, 0)).ShouldBe(0);       // outside, through all four vertices
		}
	}

	[Test]
	public void A_ray_grazing_a_curve_s_turning_point_does_not_cross_it()
	{
		// A quadratic bulging up to y = 5 at its middle: a ray at exactly its top touches it there without crossing.
		var shape = new Shape();
		var contour = new Contour();
		contour.Add(new QuadraticSegment(new Vector2D(-10, 0), new Vector2D(0, 10), new Vector2D(10, 0)));
		contour.Add(new LinearSegment(new Vector2D(10, 0), new Vector2D(-10, 0)));
		shape.Contours.Add(contour);

		shape.WindingAt(new Vector2D(-20, 5)).ShouldBe(0);
		Math.Abs(shape.WindingAt(new Vector2D(0, 2))).ShouldBe(1);
	}

	[Test]
	public void The_lower_counter_of_an_8_is_empty_on_a_row_through_its_vertices()
	{
		// Cascadia Code's '8' overlaps five contours at its waist; y = 390 runs through on-curve points of its lower
		// counter, which the texel grid of a 32-texel bake samples exactly.
		var font = OpenTypeFont.Load(CascadiaCode);
		var eight = font.GetOutline(font.GetGlyphIndex('8'));
		eight.WindingAt(new Vector2D(600, 390)).ShouldBe(0);
	}

	[Test]
	public void Every_glyph_of_the_default_fontset_bakes_to_a_field_that_reconstructs_its_exact_fill_where_a_renderer_samples_it()
	{
		var atlas = DefaultBake.Value.ToFontAtlas();
		var font = OpenTypeFont.Load(CascadiaCode);
		var failures = new List<string>();
		foreach (var cp in DefaultFontset.ResolveCharset())
		{
			if (!atlas.TryGetGlyph(cp, out var glyph) || !glyph.HasGeometry)
				continue;
			var bad = CountMisreadSamples(atlas, glyph, font.GetOutline(font.GetGlyphIndex(cp)), font.UnitsPerEm, out var where);
			if (bad > 0)
				failures.Add($"'{(char)cp}': {bad} sample(s), first at {where}");
		}

		failures.ShouldBeEmpty();
	}

	// Bilinearly sample a glyph's cell five times per texel each way, as a renderer filters it, and count the samples whose
	// reconstructed side (the median of the interpolated RGB) disagrees with the outline's exact fill, skipping those
	// within half a texel of the true edge (by the interpolated true-distance channel), where the ramp straddles it.
	private static int CountMisreadSamples(FontAtlas atlas, Glyph glyph, Shape outline, int unitsPerEm, out string where)
	{
		where = "";
		var page = atlas.Image.Pixels.Span;
		var width = atlas.Width;
		var x0 = (int)MathF.Round(glyph.U0 * width);
		var y0 = (int)MathF.Round(glyph.V0 * atlas.Height);
		var cellW = (int)MathF.Round(glyph.U1 * width) - x0;
		var cellH = (int)MathF.Round(glyph.V1 * atlas.Height) - y0;
		var unitsPerTexel = unitsPerEm / (double)atlas.BakeSize;
		var boxLeft = glyph.PlaneLeft * (double)unitsPerEm;
		var boxTop = -glyph.PlaneTop * (double)unitsPerEm;

		var bad = 0;
		const int steps = 5;
		for (var sy = 0; sy <= (cellH - 1) * steps; sy++)
		{
			for (var sx = 0; sx <= (cellW - 1) * steps; sx++)
			{
				var tx = sx / (double)steps;
				var ty = sy / (double)steps;
				var (median, trueValue) = Sample(page, width, x0, y0, cellW, cellH, tx, ty);
				if (Math.Abs(trueValue - 0.5) * atlas.DistanceRange < 0.5)
					continue;
				var inside = outline.WindingAt(new Vector2D(boxLeft + (tx + 0.5) * unitsPerTexel, boxTop - (ty + 0.5) * unitsPerTexel)) != 0;
				if (median > 0.5 == inside)
					continue;
				if (bad++ == 0)
					where = $"texel ({tx:0.#},{ty:0.#}) of the cell, {(inside ? "inside" : "outside")} the outline";
			}
		}

		return bad;
	}

	private static (double Median, double True) Sample(ReadOnlySpan<byte> page, int width, int x0, int y0, int cellW, int cellH, double tx, double ty)
	{
		var ix = Math.Min((int)tx, cellW - 2);
		var iy = Math.Min((int)ty, cellH - 2);
		var fx = tx - ix;
		var fy = ty - iy;
		var topRow = ((y0 + iy) * width + x0 + ix) * 4;
		var bottomRow = topRow + width * 4;
		Span<double> c = stackalloc double[4];
		for (var ch = 0; ch < 4; ch++)
		{
			var top = page[topRow + ch] * (1 - fx) + page[topRow + 4 + ch] * fx;
			var bottom = page[bottomRow + ch] * (1 - fx) + page[bottomRow + 4 + ch] * fx;
			c[ch] = (top * (1 - fy) + bottom * fy) / 255.0;
		}

		return (Math.Max(Math.Min(c[0], c[1]), Math.Min(Math.Max(c[0], c[1]), c[2])), c[3]);
	}

	[Test]
	public void The_committed_default_atlas_is_the_bake_of_its_fontset()
	{
		// ui-default is committed rather than baked by the build (BinaryVibrance.Vysara.Text depends on Vysara, so Vysara's own build
		// cannot run it). This holds the committed pair to the fontset next to it. When it fails, re-bake with
		//   dotnet run --project BinaryVibrance.Vysara.Text.Tool -c Release -- BinaryVibrance.Vysara/Assets/Fonts/ui-default.fontset.json -o BinaryVibrance.Vysara/Assets/Fonts
		var bake = DefaultBake.Value;

		using var committedJson = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.font.json")!;
		new StreamReader(committedJson).ReadToEnd().ShouldBe(bake.FontJson);

		// The PNG's deflate stream may differ between zlib builds, so compare texels; a libm difference in the last bit
		// of a distance may move one by a step.
		var committed = FontAtlas.LoadEmbeddedDefault().Image.Pixels.Span;
		var baked = bake.ToAtlasImage().Pixels.Span;
		baked.Length.ShouldBe(committed.Length);
		var maxDiff = 0;
		for (var i = 0; i < baked.Length; i++)
			maxDiff = Math.Max(maxDiff, Math.Abs(baked[i] - committed[i]));
		maxDiff.ShouldBeLessThanOrEqualTo(1);
	}

	[Test]
	public void The_default_atlas_covers_Latin_1_and_the_common_UI_symbols()
	{
		// Latin-1 for names and accents, and the punctuation, arrows, maths and shapes UI text reaches for, so a middle dot,
		// a degree sign or an arrow draws rather than leaving a gap.
		var atlas = FontAtlas.Default;
		foreach (var c in "·°→←↑↓↔€£©™±×÷µ¿¡ÀÉÜßàéñçæø–—‘’“”•…≤≥≠≈−▲▶▼◀●✓")
		{
			atlas.TryGetGlyph(c, out var glyph).ShouldBeTrue($"'{c}' U+{(int)c:X4}");
			glyph.HasGeometry.ShouldBeTrue($"'{c}' U+{(int)c:X4}");
		}
		for (var cp = 0xC0; cp <= 0xFF; cp++)
			atlas.TryGetGlyph(cp, out _).ShouldBeTrue($"U+{cp:X4}");
	}

	[Test]
	public void An_8_rasterizes_with_an_open_lower_counter()
	{
		// Through the compositor and the CPU rasterizer at 96 px: the middle of the '8's lower counter shows the
		// background, and its waist and lower stroke show ink.
		var root = new UIRoot(new DrawListId(0)) { Content = new TextBlock { Text = "8", FontSize = 96f } };
		root.LayoutPass(new Size(96f, 128f));
		var draw = root.Render();
		var rasterizer = new SoftwareRasterizer(96, 128);
		rasterizer.Render(draw);

		// Font units (x right of the pen, y up from the baseline; 2048 to the EM) down the '8's middle, at x = 600.
		Coverage(390).ShouldBe(0f, 0.01f); // the lower counter's middle, on the row through its vertices
		Coverage(740).ShouldBe(1f, 0.01f); // the waist, where five contours overlap
		Coverage(40).ShouldBe(1f, 0.01f);  // the bottom stroke

		float Coverage(float fontUnitsUp)
		{
			var baseline = -FontAtlas.Default.Ascender * 96f;
			return rasterizer.ReadLinear((int)(600f / 2048f * 96f), (int)(baseline - fontUnitsUp / 2048f * 96f)).W;
		}
	}

	[Test]
	[Arguments(8f)]
	[Arguments(11f)]
	[Arguments(13f)]
	[Arguments(14f)]
	[Arguments(16f)]
	[Arguments(20f)]
	public void Small_text_draws_nothing_a_pixel_clear_of_its_outline_and_all_of_a_pixel_inside_it(float size)
	{
		// Every printable ASCII glyph and a line of the rest of the charset at sixteen sub-pixel phases, through the
		// compositor and the CPU rasterizer, against the outlines themselves. A pixel centre a whole pixel or more from the
		// outline is past the reach of the one-pixel ramp, so it must read the outline's side exactly: nothing outside, full
		// coverage inside. A field whose median reads a point much nearer an edge than it is (a channel taking its distance
		// from an edge's extension past its corner, between the legs of an 'n') shows here as a speck.
		const string text = "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~"
			+ "·°→←↑↓↔€£¥©®™±×÷µ¶§¿¡ÀÉÎÕÜÆØßàéîõüñçæø–—‘’“”•…‰′″≤≥≠≈−▲▶▼◀●✓";
		var font = OpenTypeFont.Load(CascadiaCode);
		var atlas = FontAtlas.Default;
		var outlines = text.Select(c => font.GetOutline(font.GetGlyphIndex(c))).ToArray();
		var bounds = outlines.Select(o => o.ComputeBounds()).ToArray();
		var unitsPerPixel = font.UnitsPerEm / (double)size;
		var width = (int)MathF.Ceiling(text.Length * atlas.Advance('M') * size) + 8;
		var height = (int)MathF.Ceiling(atlas.LineHeight * size) + 4;
		var failures = new List<string>();
		for (var phase = 0; phase < 16; phase++)
		{
			var left = 2f + phase % 4 / 4f;
			var top = 1f + phase / 4 / 4f;
			var block = new TextBlock { Text = text, FontSize = size };
			var canvas = new Canvas();
			canvas.Children.Add(block);
			Canvas.SetLeft(block, left);
			Canvas.SetTop(block, top);
			var root = new UIRoot(new DrawListId(0)) { Content = canvas };
			root.LayoutPass(new Size(width, height));
			var rasterizer = new SoftwareRasterizer(width, height);
			rasterizer.Render(root.Render());
			var pens = block.Visual.Content.ShouldBeOfType<GlyphRunContent>().Shape().Glyphs
				.Select(g => left + (g.PenXEm + g.XOffsetEm) * size).ToArray();
			var baseline = top - atlas.Ascender * size;

			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var inside = false;
					var nearest = double.MaxValue; // to the nearest edge, in font units
					var glyph = -1;
					var at = default(Vector2D);
					for (var g = 0; g < outlines.Length; g++)
					{
						var p = new Vector2D((x + 0.5 - pens[g]) * unitsPerPixel, (baseline - y - 0.5) * unitsPerPixel);
						var b = bounds[g];
						if (p.X < b.Left - unitsPerPixel || p.X > b.Right + unitsPerPixel || p.Y < b.Bottom - unitsPerPixel || p.Y > b.Top + unitsPerPixel)
							continue;
						inside |= outlines[g].WindingAt(p) != 0;
						foreach (var contour in outlines[g].Contours)
						{
							foreach (var edge in contour.Edges)
							{
								var d = Math.Abs(edge.SignedDistanceTo(p, out _).Distance);
								if (d >= nearest)
									continue;
								nearest = d;
								glyph = g;
								at = p;
							}
						}
					}
					if (nearest < unitsPerPixel)
						continue; // within the ramp's reach of an edge
					var drawn = rasterizer.ReadLinear(x, y).W;
					if (inside ? drawn < 0.98f : drawn > 0.02f)
						failures.Add(glyph < 0
							? $"({x},{y}) at phase {phase}, a pixel clear of every glyph: drew {drawn:0.##}"
							: $"'{text[glyph]}' at ({at.X:0},{at.Y:0}) in font units, phase {phase}, {(inside ? "inside" : "outside")} by {nearest / unitsPerPixel:0.##} px: drew {drawn:0.##}");
				}
			}
		}

		failures.ShouldBeEmpty();
	}
}
