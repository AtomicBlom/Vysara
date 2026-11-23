using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Media.Imaging;
using BinaryVibrance.Vysara.Rasterizer;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The <see cref="UIPrimKind.Vector"/> prim through the CPU oracle: coverage is a one-pixel ramp centred on each edge,
/// the same at 0° and 45° (and so for RoundedRect, which moved to the same AA); fill edges land where they should; a
/// translucent stroke drawn as a group has equal crossing and non-crossing pixels; the colour is multiplied by the
/// command's texture; large paths split across index windows; and drawing a tessellated path moved, recoloured or
/// trimmed allocates nothing.
/// </summary>
public sealed class VectorPrimTests
{
	private const int Size = 100;
	private static readonly Rect Surface = new(0, 0, Size, Size);
	private static readonly PremultipliedHdrColor White = PremultipliedHdrColor.FromStraight(1, 1, 1, 1);

	private static TessellatedPath Fill(string markup, TessellationScale? scale = null)
	{
		var output = new TessellatedPath();
		new Tessellator().Fill(Geometry.Parse(markup).CompositionPath, scale ?? TessellationScale.Identity, output);
		return output;
	}

	private static TessellatedPath Stroke(string markup, float thickness, CompositionStrokeLineJoin join = CompositionStrokeLineJoin.Miter)
	{
		var output = new TessellatedPath();
		new Tessellator().Stroke(Geometry.Parse(markup).CompositionPath, new CompositionStrokeStyle { Thickness = thickness, LineJoin = join },
			TessellationScale.Identity, output);
		return output;
	}

	private static byte[] Render(DrawListData draw, int size = Size)
	{
		var raster = new SoftwareRasterizer(size, size);
		raster.Clear(Vector4.Zero);
		raster.Render(draw);
		return raster.ResolveToRgba8();
	}

	private static float Alpha(byte[] px, int x, int y, int size = Size) => px[(y * size + x) * 4 + 3] / 255f;

	private static (byte R, byte G, byte B, byte A) Pixel(byte[] px, int x, int y) =>
		(px[(y * Size + x) * 4], px[(y * Size + x) * 4 + 1], px[(y * Size + x) * 4 + 2], px[(y * Size + x) * 4 + 3]);

	// Two pixels agree within one 8-bit step. Half alpha resolves to 127.5, so float noise of an ulp in either pixel's
	// coverage picks which way it rounds; blending twice would differ by a quarter of the alpha, 64 steps.
	private static void ShouldMatchWithinAStep((byte R, byte G, byte B, byte A) actual, (byte R, byte G, byte B, byte A) expected)
	{
		var delta = Math.Max(Math.Max(Math.Abs(actual.R - expected.R), Math.Abs(actual.G - expected.G)),
			Math.Max(Math.Abs(actual.B - expected.B), Math.Abs(actual.A - expected.A)));
		delta.ShouldBeLessThanOrEqualTo(1, $"{actual} should be within one step of {expected}");
	}

	// Check every pixel near the square [30,70]², away from its corners, against a one-pixel ramp centred on the edge:
	// coverage = clamp(0.5 + d) for d the pixel centre's distance inside. `toLocal` maps surface to the square's space.
	private static int CheckRamp(byte[] px, Matrix3x2 toLocal, float lo, float hi, bool insideOnly)
	{
		var partial = 0;
		for (var y = 0; y < Size; y++)
		{
			for (var x = 0; x < Size; x++)
			{
				var p = Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), toLocal);
				var d = MathF.Min(MathF.Min(p.X - lo, hi - p.X), MathF.Min(p.Y - lo, hi - p.Y));
				var centre = (lo + hi) / 2;
				var nearCorner = MathF.Abs(p.X - centre) > (hi - lo) / 2 - 6 && MathF.Abs(p.Y - centre) > (hi - lo) / 2 - 6;
				if (MathF.Abs(d) > 1.5f || nearCorner || (insideOnly && d < 0))
					continue;
				var expected = Math.Clamp(0.5f + d, 0f, 1f);
				Alpha(px, x, y).ShouldBe(expected, 2.5f / 255f, $"pixel ({x},{y}) at {d:0.###} px from the edge");
				if (expected is > 0.05f and < 0.95f)
					partial++;
			}
		}
		return partial;
	}

	[Test]
	[Arguments(0f)]
	[Arguments(45f)]
	[Arguments(30f)]
	public void A_vector_edges_ramp_is_one_pixel_centred_on_the_edge_at_any_angle(float degrees)
	{
		// Edges off the pixel grid, so even the unrotated square has partly covered pixels to measure.
		var rotate = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f, new Vector2(50, 50));
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(Fill("M 30.3,30.3 L 69.7,30.3 69.7,69.7 30.3,69.7 Z"), rotate, White, Surface);
		var px = Render(builder.End());

		Matrix3x2.Invert(rotate, out var toLocal).ShouldBeTrue();
		CheckRamp(px, toLocal, 30.3f, 69.7f, insideOnly: false).ShouldBeGreaterThan(20);
	}

	[Test]
	[Arguments(0f)]
	[Arguments(45f)]
	public void A_rounded_rects_ramp_is_the_same_at_0_and_45_degrees(float degrees)
	{
		var border = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(HdrColor.FromRgb(255, 255, 255)) };
		border.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f, new Vector2(20, 20)) };
		var canvas = new Canvas();
		canvas.Children.Add(border);
		Canvas.SetLeft(border, 30);
		Canvas.SetTop(border, 30);
		var root = new UIRoot(new DrawListId(0)) { Content = canvas };
		root.LayoutPass(new Size(Size, Size));
		var px = Render(root.Render());

		// The quad ends at the rect's edge, so only the inside half of the ramp is drawn; it must match at both angles.
		// The border's visual maps its local [0,40]² to the surface; CheckRamp measures against [30,70]².
		Matrix3x2.Invert(border.Visual.Transform, out var toLocal).ShouldBeTrue();
		CheckRamp(px, toLocal * Matrix3x2.CreateTranslation(30, 30), 30, 70, insideOnly: true).ShouldBeGreaterThan(degrees == 0 ? -1 : 20);
	}

	[Test]
	public void Fill_edges_land_where_they_should()
	{
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(Fill("M 10.25,20 L 30.75,20 30.75,40.5 10.25,40.5 Z"), Matrix3x2.Identity, White, Surface);
		var px = Render(builder.End());

		// An axis-aligned edge's coverage is the pixel's area inside it.
		Alpha(px, 9, 30).ShouldBe(0f);
		Alpha(px, 10, 30).ShouldBe(0.75f, 1.5f / 255f);
		Alpha(px, 11, 30).ShouldBe(1f);
		Alpha(px, 30, 30).ShouldBe(0.75f, 1.5f / 255f);
		Alpha(px, 31, 30).ShouldBe(0f);
		Alpha(px, 20, 19).ShouldBe(0f);
		Alpha(px, 20, 20).ShouldBe(1f, 1.5f / 255f); // the edge at y = 20 is the pixel's top: fully inside
		Alpha(px, 20, 40).ShouldBe(0.5f, 1.5f / 255f);
		// And nothing double-covers: the interior is exactly opaque white, with no seam between interior and fringe.
		for (var y = 21; y < 40; y++)
		{
			for (var x = 11; x < 30; x++)
				Pixel(px, x, y).ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
		}
	}

	[Test]
	public void A_built_texture_with_no_uv_map_is_stretched_over_the_paths_bounds()
	{
		var image = BitmapSource.FromRgba8(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]);
		var path = Fill("M 10,20 L 50,20 50,60 10,60 Z");
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(path, PathTrim.None, Matrix3x2.Identity, White, Surface, texture: image);
		var draw = builder.End();

		var min = new Vector2(float.MaxValue);
		var max = new Vector2(float.MinValue);
		foreach (var v in draw.Vertices.Span)
		{
			min = Vector2.Min(min, v.Uv);
			max = Vector2.Max(max, v.Uv);
		}
		min.X.ShouldBe(0f, 1e-5f);
		min.Y.ShouldBe(0f, 1e-5f);
		max.X.ShouldBe(1f, 1e-5f);
		max.Y.ShouldBe(1f, 1e-5f);
	}

	[Test]
	public void A_translucent_mesh_blends_once_along_its_shared_edges()
	{
		// A translucent 40×40 quad: its diagonal runs through pixel centres, which the top-left rule gives to one triangle.
		var half = PremultipliedHdrColor.FromStraight(1, 1, 1, 0.5f);
		var draw = new DrawListData
		{
			Vertices = new OverlayVertex[]
			{
				new(new Vector2(10, 10), Vector2.Zero, half, Vector4.Zero),
				new(new Vector2(50, 10), Vector2.Zero, half, Vector4.Zero),
				new(new Vector2(50, 50), Vector2.Zero, half, Vector4.Zero),
				new(new Vector2(10, 50), Vector2.Zero, half, Vector4.Zero),
			},
			Indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
			Commands = new DrawCommand[] { new(Surface, 0, 0, 6, 0, (uint)UIPrimKind.Fill) },
			CoordinateSize = new Vector2(Size),
		};
		var px = Render(draw);

		for (var i = 10; i < 50; i++)
			Alpha(px, i, i).ShouldBe(0.5f, 1f / 255f, $"diagonal pixel {i}");
		Alpha(px, 20, 30).ShouldBe(0.5f, 1f / 255f);
	}

	[Test]
	[Arguments(13.3f, 13.7f, 29.5f, 29f)] // each triangle's own view of the edge rounds pixel (20, 20) outside it
	[Arguments(28f, 29.5f, 14.5f, 13.3f)] // and here inside it
	public void A_shared_edge_through_a_pixel_centre_draws_it_exactly_once(float ax, float ay, float bx, float by)
	{
		// A translucent quad split along a to b, a diagonal passing within rounding of pixel (20, 20)'s centre. Evaluated
		// from each triangle's own end, the edge function rounds the same way in both rather than to opposite values, so
		// the pixel would be dropped or blended twice; the fill rule evaluates it one way round, as a GPU's exact
		// fixed-point arithmetic does, and gives it to one triangle.
		var a = new Vector2(ax, ay);
		var b = new Vector2(bx, by);
		var across = 10f * Vector2.Normalize(new Vector2(a.Y - b.Y, b.X - a.X));
		var half = PremultipliedHdrColor.FromStraight(1, 1, 1, 0.5f);
		var draw = new DrawListData
		{
			Vertices = new OverlayVertex[]
			{
				new(a, Vector2.Zero, half, Vector4.Zero),
				new((a + b) / 2 + across, Vector2.Zero, half, Vector4.Zero),
				new(b, Vector2.Zero, half, Vector4.Zero),
				new((a + b) / 2 - across, Vector2.Zero, half, Vector4.Zero),
			},
			Indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
			Commands = new DrawCommand[] { new(Surface, 0, 0, 6, 0, (uint)UIPrimKind.Fill) },
			CoordinateSize = new Vector2(Size),
		};

		Alpha(Render(draw), 20, 20).ShouldBe(0.5f, 1f / 255f);
	}

	[Test]
	public void Pixels_on_the_spokes_of_a_round_cap_are_drawn()
	{
		// Kaldra's GPU run found these two pixels inside a round start cap, on a spoke of its fan, left empty by the oracle:
		// the wedges either side of the spoke each computed its rim point, and the copies rounded a hairline apart.
		var path = new CompositionPath();
		path.MoveTo(new Vector2(480, 190));
		path.LineTo(new Vector2(560, 70));
		path.LineTo(new Vector2(640, 190));
		path.LineTo(new Vector2(720, 70));
		path.LineTo(new Vector2(800, 190));
		var style = new CompositionStrokeStyle { Thickness = 18, LineJoin = CompositionStrokeLineJoin.Bevel, StartCap = CompositionStrokeCap.Round, EndCap = CompositionStrokeCap.Triangle };
		var stroke = new TessellatedPath();
		new Tessellator().Stroke(path, style, TessellationScale.For(Matrix3x2.Identity), stroke);
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(1280, 720));
		builder.AddVector(stroke, Matrix3x2.Identity, White, new Rect(0, 0, 1280, 720));
		var raster = new SoftwareRasterizer(1280, 720);
		raster.Clear(new Vector4(0, 0, 0, 1));
		raster.Render(builder.End());
		var px = raster.ResolveToRgba8();

		foreach (var (x, y) in new[] { (477, 190), (472, 191) })
			px[(y * 1280 + x) * 4].ShouldBe((byte)255, $"pixel ({x},{y})");
	}

	[Test]
	public void A_translucent_stroke_drawn_as_a_group_has_equal_crossing_and_non_crossing_pixels()
	{
		var crossing = Stroke("M 10,50 L 90,50 M 50,10 L 50,90", 8);
		var joined = Stroke("M 10,10 L 90,50 L 10,90", 10);
		var red = PremultipliedHdrColor.FromStraight(1, 0, 0, 1);

		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(crossing, PathTrim.None, Matrix3x2.Identity, red, Surface, groupOpacity: 0.5f);
		var px = Render(builder.End());
		ShouldMatchWithinAStep(Pixel(px, 50, 50), Pixel(px, 20, 50)); // at the crossing and on one arm
		Alpha(px, 20, 50).ShouldBe(0.5f, 1f / 255f);

		builder.Begin(new Vector2(Size));
		builder.AddVector(joined, PathTrim.None, Matrix3x2.Identity, red, Surface, groupOpacity: 0.5f);
		px = Render(builder.End());
		ShouldMatchWithinAStep(Pixel(px, 80, 50), Pixel(px, 30, 20)); // where the join's two bodies overlap, and on one arm

		// Folded into the vertex colour instead, the crossing blends twice: why the group exists.
		builder.Begin(new Vector2(Size));
		builder.AddVector(crossing, Matrix3x2.Identity, PremultipliedHdrColor.FromStraight(1, 0, 0, 0.5f), Surface);
		px = Render(builder.End());
		Alpha(px, 50, 50).ShouldBe(0.75f, 1f / 255f);
	}

	[Test]
	public void The_colour_is_multiplied_by_the_commands_texture()
	{
		// A 2×2 image mapped onto the square [20,60]²: each probe sits past its texel's centre, where clamped bilinear
		// filtering reads that texel alone.
		byte[] texels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 128];
		var image = BitmapSource.FromRgba8(2, 2, texels);
		var toUv = Matrix3x2.CreateTranslation(-20, -20) * Matrix3x2.CreateScale(1 / 40f);
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(Fill("M 20,20 L 60,20 60,60 20,60 Z"), PathTrim.None, Matrix3x2.Identity, White, Surface, texture: image, localToUv: toUv);
		var draw = builder.End();

		draw.TextureSlots.Count.ShouldBe(2);
		draw.Commands.Span[0].TextureIndex.ShouldBe(1);
		var px = Render(draw);
		Pixel(px, 25, 25).ShouldBe(((byte)255, (byte)0, (byte)0, (byte)255));
		Pixel(px, 55, 25).ShouldBe(((byte)0, (byte)255, (byte)0, (byte)255));
		Pixel(px, 25, 55).ShouldBe(((byte)0, (byte)0, (byte)255, (byte)255));
		Alpha(px, 55, 55).ShouldBe(128 / 255f, 1f / 255f);
	}

	[Test]
	public void A_path_past_65535_vertices_splits_across_index_windows()
	{
		var style = new CompositionStrokeStyle { Thickness = 1 };
		style.SetDashArray([5, 5]);
		var path = new TessellatedPath();
		new Tessellator().Stroke(Geometry.Parse("M 0,0 L 40000,0").CompositionPath, style, TessellationScale.Identity, path);
		path.Vertices.Length.ShouldBeGreaterThan(65535);

		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(path, Matrix3x2.CreateScale(Size / 40000f, 1) * Matrix3x2.CreateTranslation(0, 50), White, Surface);
		var draw = builder.End();

		var commands = draw.Commands.Span;
		commands.Length.ShouldBeGreaterThan(1);
		var indices = draw.Indices.Span;
		var drawn = 0;
		foreach (var cmd in commands)
		{
			cmd.Tag.ShouldBe((uint)UIPrimKind.Vector);
			(cmd.IndexCount % 3).ShouldBe(0);
			for (var i = cmd.IndexOffset; i < cmd.IndexOffset + cmd.IndexCount; i++)
			{
				indices[i].ShouldBeLessThan((ushort)65535);
				(cmd.VertexOffset + indices[i]).ShouldBeLessThan(draw.Vertices.Length);
			}
			drawn += cmd.IndexCount;
		}
		drawn.ShouldBe(path.Vertices.Length);
		commands[1].VertexOffset.ShouldBeGreaterThan(0);

		var raster = new SoftwareRasterizer(Size, Size);
		raster.Render(draw);
		raster.TrianglesDrawn.ShouldBe(path.TriangleCount);
	}

	[Test]
	public void Drawing_a_path_moved_recoloured_and_trimmed_allocates_nothing_once_warm()
	{
		var fill = Fill("M 0,0 C 0,40 40,40 40,0 Z");
		var stroke = Stroke("M 0,0 C 0,40 40,40 40,0 L 20,-20 Z", 3, CompositionStrokeLineJoin.Round);
		var builder = new DrawListBuilder();
		for (var i = 0; i < 8; i++)
			Frame(i);

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 200; i++)
				Frame(i);
		});

		void Frame(int i)
		{
			builder.Begin(new Vector2(Size));
			var move = Matrix3x2.CreateTranslation(i % 50, 10);
			var color = PremultipliedHdrColor.FromStraight(i / 200f, 0.5f, 1, 1);
			builder.AddVector(fill, move, color, Surface);
			builder.AddVector(stroke, new PathTrim(0, (i % 20) / 20f, i / 37f), move, color, Surface, groupOpacity: 0.6f);
			_ = builder.End();
		}
	}

	[Test]
	public void A_vector_lists_dump_shows_the_prim_and_its_distances()
	{
		// A 2-thick line from x = 1 to 9 with flat caps: start extension, body, end extension, each half a pixel past the
		// sides (y = 3.5, 6.5) and the caps carrying the end lines x = 1 and 9 as clips.
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(10, 10));
		builder.AddVector(Stroke("M 1,5 L 9,5", 2), Matrix3x2.Identity, White, new Rect(0, 0, 10, 10));
		var dump = builder.End().Dump();

		dump.ShouldBe("""
			DrawListData (rev 1, texrev 1, coords 10x10, hdr=False)
			  Textures (1):
			    [0] White 1x1 v0 premultiplied linear
			  Vertices (18):
			    [0] pos=(0.5,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,-0.5,1000000)
			    [1] pos=(1,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,0,1000000)
			    [2] pos=(1,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,0,1000000)
			    [3] pos=(0.5,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,-0.5,1000000)
			    [4] pos=(1,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,0,1000000)
			    [5] pos=(0.5,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,-0.5,1000000)
			    [6] pos=(1,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,0,8)
			    [7] pos=(9,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,8,0)
			    [8] pos=(9,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,8,0)
			    [9] pos=(1,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,0,8)
			    [10] pos=(9,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,8,0)
			    [11] pos=(1,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,0,8)
			    [12] pos=(9,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,1000000,0)
			    [13] pos=(9.5,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,1000000,-0.5)
			    [14] pos=(9.5,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,1000000,-0.5)
			    [15] pos=(9,6.5) uv=(0,0) col=(1,1,1,1) custom=(-0.5,2.5,1000000,0)
			    [16] pos=(9.5,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,1000000,-0.5)
			    [17] pos=(9,3.5) uv=(0,0) col=(1,1,1,1) custom=(2.5,-0.5,1000000,0)
			  Indices (18): 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17
			  Commands (1):
			    [0] tag=7(Vector) tex=0 clip=(0,0 10x10) idx=0+18 vtx=0

			""".ReplaceLineEndings("\n"));
	}
}
