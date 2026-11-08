using System.Numerics;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Media.Imaging;
using SUIsei.Rasterizer;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="ShapeContent"/>, the composition content of a vector shape: it dumps its path, paint, stroke style and trim;
/// its version moves with what it holds; its tessellations are cached on it and kept while the shape moves, fades,
/// recolours or is trimmed; and the compositor draws it through the Vector prim, with gradients cut exactly at their stops,
/// images sampled through the prim's texture, a translucent stroke that can overlap itself drawn in a layer of its own,
/// and a faded shape with a fill and a stroke faded as one.
/// </summary>
public sealed class ShapeContentTests
{
	private const int Size = 100;
	private static readonly DrawListId Surface = new(0);
	private static readonly HdrColor Red = HdrColor.FromRgb(255, 0, 0);
	private static readonly HdrColor Green = HdrColor.FromRgb(0, 255, 0);
	private static readonly HdrColor Blue = HdrColor.FromRgb(0, 0, 255);

	private static CompositionPath PathOf(string markup) => Geometry.Parse(markup).CompositionPath;

	private static (RootVisual Root, SpriteVisual Sprite) Scene(ShapeContent shape)
	{
		var root = new RootVisual { SurfaceSize = new Vector2(Size) };
		var sprite = new SpriteVisual { Content = shape };
		root.Add(sprite);
		return (root, sprite);
	}

	private static DrawListData Composite(DrawListCompositor compositor, RootVisual root)
	{
		compositor.Composite(root, Surface);
		return compositor.LastDraw(Surface);
	}

	private static SoftwareRasterizer Raster(DrawListData draw)
	{
		var raster = new SoftwareRasterizer(Size, Size);
		raster.Clear(new Vector4(0f, 0f, 0f, 1f));
		raster.Render(draw);
		return raster;
	}

	private static void ShouldBeClose(Vector4 actual, Vector4 expected, float tolerance = 2e-3f) =>
		Vector4.Distance(actual, expected).ShouldBeLessThanOrEqualTo(tolerance, $"{actual} should be within {tolerance} of {expected}");

	private static UIPrimKind[] Kinds(DrawListData draw) => [.. draw.Commands.Span.ToArray().Select(static c => (UIPrimKind)c.Tag)];

	[Test]
	public void A_shape_dumps_its_path_paint_stroke_style_and_trim()
	{
		var shape = new ShapeContent
		{
			Path = PathOf("M 10,10 L 90,10 L 90,90 Z"),
			PathTransform = Matrix3x2.CreateTranslation(1f, 2f),
			Fill = new CompositionColorBrush(Red),
			Stroke = new CompositionColorBrush(Blue) { Opacity = 0.5f },
			Trim = new PathTrim(0f, 0.5f, 0.25f),
		};
		shape.StrokeStyle.Thickness = 4f;
		shape.StrokeStyle.LineJoin = PenLineJoin.Round;
		shape.StrokeStyle.StartCap = PenLineCap.Round;
		shape.StrokeStyle.SetDashArray([2f, 1f]);
		shape.StrokeStyle.DashOffset = 0.5f;

		new SpriteVisual { Content = shape }.Dump().ShouldBe(
			"SpriteVisual xform=[0,0] op=1 shape path=\"M 10,10 L 90,10 L 90,90 Z\" xform=[1,0,0,1,1,2] fill=(1,0,0,1) " +
			"stroke=4(0,0,1,1)*0.5 join=Round caps=Round,Flat dashes=2,1@0.5 trim=0..0.5+0.25\n");
		new SpriteVisual { Content = new ShapeContent { Path = PathOf("M 0,0 L 1,1") } }.Dump()
			.ShouldBe("SpriteVisual xform=[0,0] op=1 shape path=\"M 0,0 L 1,1\"\n");
	}

	/// <summary>A dash pattern too fine for its path draws the rest of the stroke solid past the dash limit, and the dump
	/// says so once the stroke is tessellated.</summary>
	[Test]
	public void A_dash_pattern_past_the_limit_says_so_in_the_dump()
	{
		var shape = new ShapeContent { Path = PathOf("M 0,0 L 1000000,0"), Stroke = new CompositionColorBrush(Blue) };
		shape.StrokeStyle.Thickness = 0.01f;
		shape.StrokeStyle.SetDashArray([0.5f, 0.5f]);
		var visual = new SpriteVisual { Content = shape };
		visual.Dump().ShouldNotContain("dash limit"); // not tessellated yet

		shape.TessellateStroke(TessellationScale.Identity)!.DashesLimited.ShouldBeTrue();
		visual.Dump().ShouldEndWith(" dashes=0.5,0.5 (past the dash limit, the rest drawn solid)\n");

		shape.StrokeStyle.SetDashArray([5000f, 5000f]); // 50 units each at this thickness: 10,000 dashes, within the limit
		shape.TessellateStroke(TessellationScale.Identity);
		visual.Dump().ShouldNotContain("dash limit");
	}

	[Test]
	public void The_version_moves_with_what_the_content_holds_and_not_with_its_retained_parts()
	{
		var path = PathOf("M 0,0 L 10,0 L 10,10 Z");
		var shape = new ShapeContent { Path = path, Fill = new CompositionColorBrush(Red) };
		var version = shape.Version;

		shape.Path = path;
		shape.Trim = PathTrim.None;
		shape.PathTransform = Matrix3x2.Identity;
		shape.Version.ShouldBe(version);

		shape.StrokeStyle.Thickness = 3f;
		((CompositionColorBrush)shape.Fill).Color = Blue;
		shape.Version.ShouldBe(version); // versioned by the style and the brush themselves

		shape.Trim = new PathTrim(0f, 0.5f, 0f);
		shape.Version.ShouldBe(version + 1);
		shape.Stroke = new CompositionColorBrush(Green);
		shape.PathTransform = Matrix3x2.CreateScale(2f);
		shape.Version.ShouldBe(version + 3);
	}

	[Test]
	public void Bounds_are_the_paths_through_its_transform()
	{
		var shape = new ShapeContent { Path = PathOf("M 0,0 C 0,10 10,10 10,0") };
		shape.Bounds.ShouldBeNear(new Rect(0, 0, 10, 7.5f), 1e-4f);
		shape.PathTransform = Matrix3x2.CreateScale(2f) * Matrix3x2.CreateTranslation(5f, 5f);
		shape.Bounds.ShouldBeNear(new Rect(5, 5, 20, 15), 1e-4f);
		new ShapeContent().Bounds.ShouldBe(Rect.Zero);
	}

	[Test]
	public void The_tessellation_is_kept_while_the_shape_moves_fades_recolours_or_is_trimmed()
	{
		var paint = new CompositionColorBrush(Red);
		var shape = new ShapeContent { Path = PathOf("M 10,10 L 60,10 L 60,60"), Fill = paint, Stroke = paint };
		shape.StrokeStyle.Thickness = 4f;
		var (root, sprite) = Scene(shape);
		var compositor = new DrawListCompositor();
		Composite(compositor, root);
		var fill = shape.TessellateFill(TessellationScale.Identity)!;
		var stroke = shape.TessellateStroke(TessellationScale.Identity)!;
		var (fillVersion, strokeVersion) = (fill.Version, stroke.Version);

		sprite.Transform = Matrix3x2.CreateTranslation(7f, 3f);
		sprite.Opacity = 0.5f;
		paint.Color = Blue;
		shape.Trim = new PathTrim(0.1f, 0.6f, 0f);
		Composite(compositor, root);
		(fill.Version, stroke.Version).ShouldBe((fillVersion, strokeVersion));

		// A new scale bucket, a new stroke style, a new path transform or another path re-tessellates.
		sprite.Transform = Matrix3x2.CreateScale(4f);
		Composite(compositor, root);
		stroke.Version.ShouldBe(strokeVersion + 1);
		shape.StrokeStyle.Thickness = 2f;
		sprite.MarkDirty(); // a change made inside content is marked by whoever makes it, as an element does
		Composite(compositor, root);
		stroke.Version.ShouldBe(strokeVersion + 2);
		fill.Version.ShouldBe(fillVersion + 1);
		shape.PathTransform = Matrix3x2.CreateTranslation(1f, 0f);
		sprite.MarkDirty();
		Composite(compositor, root);
		(fill.Version, stroke.Version).ShouldBe((fillVersion + 2, strokeVersion + 3));
		shape.Path = PathOf("M 10,10 L 60,10 L 60,60");
		sprite.MarkDirty();
		Composite(compositor, root);
		(fill.Version, stroke.Version).ShouldBe((fillVersion + 3, strokeVersion + 4));
	}

	[Test]
	public void A_foreign_compositor_can_draw_the_cached_tessellation_itself()
	{
		var shape = new ShapeContent { Path = PathOf("M 20,20 L 80,20 L 80,80 L 20,80 Z") };
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(shape.TessellateFill(TessellationScale.Identity)!, Matrix3x2.Identity, Red.ToPremultiplied(), new Rect(0, 0, Size, Size));
		var raster = Raster(builder.End());

		ShouldBeClose(raster.ReadLinear(50, 50), Red.ToPremultipliedVector());
		new ShapeContent().TessellateFill(TessellationScale.Identity).ShouldBeNull();
	}

	[Test]
	public void Fill_and_stroke_draw_through_the_vector_prim_and_land_where_they_should()
	{
		var shape = new ShapeContent
		{
			Path = PathOf("M 20,20 L 80,20 L 80,80 L 20,80 Z"),
			Fill = new CompositionColorBrush(Red),
			Stroke = new CompositionColorBrush(Blue),
		};
		shape.StrokeStyle.Thickness = 4f;
		var draw = Composite(new DrawListCompositor(), Scene(shape).Root);

		Kinds(draw).ShouldAllBe(k => k == UIPrimKind.Vector);
		var raster = Raster(draw);
		ShouldBeClose(raster.ReadLinear(50, 50), Red.ToPremultipliedVector());
		// The stroke spans y 18..22 along the top edge: rows 18 to 21 are covered, 17 and 22's neighbours are not.
		ShouldBeClose(raster.ReadLinear(50, 18), Blue.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(50, 21), Blue.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(50, 17), new Vector4(0f, 0f, 0f, 1f));
		ShouldBeClose(raster.ReadLinear(50, 22), Red.ToPremultipliedVector());
	}

	[Test]
	public void A_translucent_stroke_that_can_cross_itself_is_drawn_opaque_in_a_layer_carrying_its_alpha()
	{
		var glass = new HdrColor(0.2f, 0.6f, 1f, 0.5f);
		var crossing = new ShapeContent { Path = PathOf("M 10,50 L 90,50 M 50,10 L 50,90"), Stroke = new CompositionColorBrush(glass) };
		crossing.StrokeStyle.Thickness = 6f;
		var draw = Composite(new DrawListCompositor(), Scene(crossing).Root);

		Kinds(draw).ShouldBe([UIPrimKind.BeginLayer, UIPrimKind.Vector, UIPrimKind.EndLayer]);
		var raster = Raster(draw);
		raster.ReadLinear(50, 50).ShouldBe(raster.ReadLinear(30, 50)); // the crossing blends once
		ShouldBeClose(raster.ReadLinear(30, 50), glass.ToPremultipliedVector() + new Vector4(0f, 0f, 0f, 1f) * 0.5f);

		// One segment cannot overlap itself, so it blends per vertex with no layer.
		var single = new ShapeContent { Path = PathOf("M 10,50 L 90,50"), Stroke = new CompositionColorBrush(glass) };
		single.StrokeStyle.Thickness = 6f;
		Kinds(Composite(new DrawListCompositor(), Scene(single).Root)).ShouldBe([UIPrimKind.Vector]);
	}

	[Test]
	public void A_gradient_stroke_with_one_alpha_takes_a_layer_and_one_whose_alpha_varies_does_not()
	{
		var uniform = new CompositionLinearGradientBrush { InterpolationSpace = CompositionColorSpace.RgbLinear };
		uniform.SetColorStops([new(0f, Red with { A = 0.5f }), new(1f, Blue with { A = 0.5f })]);
		var varying = new CompositionLinearGradientBrush { InterpolationSpace = CompositionColorSpace.RgbLinear };
		varying.SetColorStops([new(0f, Red with { A = 0.5f }), new(1f, Blue)]);

		foreach (var (paint, layered) in new[] { (uniform, true), (varying, false) })
		{
			var shape = new ShapeContent { Path = PathOf("M 10,50 L 90,50 M 50,10 L 50,90"), Stroke = paint };
			shape.StrokeStyle.Thickness = 6f;
			var draw = Composite(new DrawListCompositor(), Scene(shape).Root);
			Kinds(draw).Contains(UIPrimKind.BeginLayer).ShouldBe(layered);
			if (layered)
				draw.Vertices.Span.ToArray().Skip(4).ShouldAllBe(v => (float)v.Color.A == 1f); // past the layer's marker quad
		}
	}

	[Test]
	public void A_faded_shape_with_a_fill_and_a_stroke_fades_as_one()
	{
		var shape = new ShapeContent
		{
			Path = PathOf("M 20,20 L 80,20 L 80,80 L 20,80 Z"),
			Fill = new CompositionColorBrush(Red),
			Stroke = new CompositionColorBrush(Blue),
		};
		shape.StrokeStyle.Thickness = 8f;
		var (root, sprite) = Scene(shape);
		sprite.Opacity = 0.5f;
		var draw = Composite(new DrawListCompositor(), root);

		Kinds(draw).ShouldBe([UIPrimKind.BeginLayer, UIPrimKind.Vector, UIPrimKind.EndLayer]);
		// On the stroke's inner half the fill does not show through: half the stroke's blue over black.
		ShouldBeClose(Raster(draw).ReadLinear(50, 22), new Vector4(0f, 0f, 0.5f, 1f));
	}

	[Test]
	public void A_gradient_paints_a_shape_exactly_at_its_stops_across_the_shapes_bounds()
	{
		// The square spans x 20..81, so the middle stop falls on pixel 50's centre.
		var gradient = new CompositionLinearGradientBrush { EndPoint = new Vector2(1f, 0f), InterpolationSpace = CompositionColorSpace.RgbLinear };
		gradient.SetColorStops([new(0f, Red), new(0.5f, Green), new(1f, Blue)]);
		var shape = new ShapeContent { Path = PathOf("M 20,20 L 81,20 L 81,80 L 20,80 Z"), Fill = gradient };
		var draw = Composite(new DrawListCompositor(), Scene(shape).Root);

		var atStop = draw.Vertices.Span.ToArray().Where(v => v.Position.X == 50.5f).ToArray();
		atStop.Length.ShouldBeGreaterThanOrEqualTo(2);
		atStop.ShouldAllBe(v => v.Color == Green.ToPremultiplied());
		var raster = Raster(draw);
		ShouldBeClose(raster.ReadLinear(50, 50), Green.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(35, 50), Vector4.Lerp(Red.ToPremultipliedVector(), Green.ToPremultipliedVector(), 15.5f / 30.5f));
		// The fill's edges still antialias where they should: pixel 20 is wholly inside, 19 wholly outside.
		ShouldBeClose(raster.ReadLinear(20, 50), Vector4.Lerp(Red.ToPremultipliedVector(), Green.ToPremultipliedVector(), 0.5f / 30.5f));
		ShouldBeClose(raster.ReadLinear(19, 50), new Vector4(0f, 0f, 0f, 1f));
	}

	[Test]
	public void Gradient_pieces_of_a_vector_mesh_meet_with_no_seam()
	{
		var glass = new HdrColor(0.2f, 0.6f, 1f, 0.5f);
		var gradient = new CompositionLinearGradientBrush { EndPoint = new Vector2(1f, 1f), InterpolationSpace = CompositionColorSpace.RgbLinear };
		gradient.SetColorStops([new(0f, glass), new(0.3f, glass), new(0.55f, glass), new(1f, glass)]);
		var ellipse = new EllipseGeometry { Center = new Point(50, 50), RadiusX = 40, RadiusY = 30 }.CompositionPath;

		var banded = Raster(Composite(new DrawListCompositor(), Scene(new ShapeContent { Path = ellipse, Fill = gradient }).Root)).ResolveToRgba8();
		var flat = Raster(Composite(new DrawListCompositor(), Scene(new ShapeContent { Path = ellipse, Fill = new CompositionColorBrush(glass) }).Root)).ResolveToRgba8();

		banded.ShouldBe(flat);
	}

	[Test]
	[Arguments(CompositionColorSpace.Rgb)]
	[Arguments(CompositionColorSpace.RgbLinear)]
	public void Every_triangle_of_a_mesh_colours_a_shared_corner_the_same(CompositionColorSpace space)
	{
		// sRGB interpolation is drawn as chords between exact samples; a corner inside a chord must take the chord's colour
		// in every triangle that meets it, not the exact curve's in one and the chord's in its neighbour.
		var gradient = new CompositionLinearGradientBrush { EndPoint = new Vector2(1f, 0f), InterpolationSpace = space };
		gradient.SetColorStops([new(0f, HdrColor.FromRgb(0, 0, 0)), new(1f, HdrColor.FromRgb(255, 255, 255))]);
		var ellipse = new EllipseGeometry { Center = new Point(50, 50), RadiusX = 40, RadiusY = 30 }.CompositionPath;
		var draw = Composite(new DrawListCompositor(), Scene(new ShapeContent { Path = ellipse, Fill = gradient }).Root);

		var colours = new Dictionary<Vector2, HashSet<PremultipliedHdrColor>>();
		foreach (var v in draw.Vertices.Span)
		{
			if (!colours.TryGetValue(v.Position, out var set))
				colours[v.Position] = set = [];
			set.Add(v.Color);
		}
		colours.Values.ShouldAllBe(set => set.Count == 1);
	}

	[Test]
	public void An_image_paints_a_shape_through_the_vector_prims_texture()
	{
		// A 2×2 image over the square [20,60]²: each probe sits past its texel's centre, where clamped bilinear filtering
		// reads that texel alone.
		byte[] texels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
		var image = new CompositionSurfaceBrush { Surface = BitmapSource.FromRgba8(2, 2, texels) };
		var shape = new ShapeContent { Path = PathOf("M 20,20 L 60,20 L 60,60 L 20,60 Z"), Fill = image };
		var draw = Composite(new DrawListCompositor(), Scene(shape).Root);

		draw.Commands.Span[0].TextureIndex.ShouldBe(1);
		var px = Raster(draw).ResolveToRgba8();
		(px[(25 * Size + 25) * 4], px[(25 * Size + 25) * 4 + 1]).ShouldBe(((byte)255, (byte)0));
		(px[(25 * Size + 55) * 4], px[(25 * Size + 55) * 4 + 1]).ShouldBe(((byte)0, (byte)255));
		px[(55 * Size + 25) * 4 + 2].ShouldBe((byte)255);
	}

	[Test]
	[Arguments(0f)]
	[Arguments(45f)]
	public void A_letterboxed_image_paints_only_where_it_lies_under_any_transform(float degrees)
	{
		// A square image in a 40×20 box, Uniform: it fills x 30..50 and leaves the rest of the box unpainted, turned or not.
		var image = new CompositionSurfaceBrush { Surface = BitmapSource.FromRgba8(1, 1, [255, 255, 255, 255]), Stretch = Stretch.Uniform };
		var shape = new ShapeContent { Path = PathOf("M 20,40 L 60,40 L 60,60 L 20,60 Z"), Fill = image };
		var (root, sprite) = Scene(shape);
		var turn = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f, new Vector2(40f, 50f));
		sprite.Transform = turn;
		var raster = Raster(Composite(new DrawListCompositor(), root));

		raster.ReadLinear(40, 50).X.ShouldBe(1f, 1e-3f);
		foreach (var letterbox in new[] { new Vector2(24.5f, 50.5f), new Vector2(55.5f, 44.5f) })
		{
			var p = Vector2.Transform(letterbox, turn);
			raster.ReadLinear((int)p.X, (int)p.Y).X.ShouldBe(0f);
		}
	}

	[Test]
	public void An_image_stroke_paints_only_over_its_outlines_bounds()
	{
		// The image maps over the outline's bounds, as a gradient does, and paints nothing past them: the outer half of the
		// square's stroke is left transparent, and a horizontal line, whose bounds have no height, draws nothing.
		var image = new CompositionSurfaceBrush { Surface = BitmapSource.FromRgba8(1, 1, [255, 255, 255, 255]) };
		var square = new ShapeContent { Path = PathOf("M 20,20 L 60,20 L 60,60 L 20,60 Z"), Stroke = image };
		square.StrokeStyle.Thickness = 8f;
		var raster = Raster(Composite(new DrawListCompositor(), Scene(square).Root));
		raster.ReadLinear(21, 40).X.ShouldBe(1f, 1e-3f);
		raster.ReadLinear(17, 40).X.ShouldBe(0f);

		var line = new ShapeContent { Path = PathOf("M 20,50 L 80,50"), Stroke = image };
		line.StrokeStyle.Thickness = 8f;
		Composite(new DrawListCompositor(), Scene(line).Root).Commands.Length.ShouldBe(0);
	}

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Moving_fading_recolouring_and_trimming_a_shape_allocates_nothing_once_warm()
	{
		var paint = new CompositionColorBrush(Red);
		var gradient = new CompositionLinearGradientBrush { EndPoint = new Vector2(1f, 0f) };
		gradient.SetColorStops([new(0f, Red), new(1f, Blue with { A = 0.5f })]);
		var shape = new ShapeContent { Path = PathOf("M 10,10 C 10,90 90,90 90,10 L 50,50 Z"), Fill = gradient, Stroke = paint };
		shape.StrokeStyle.Thickness = 3f;
		shape.StrokeStyle.SetDashArray([4f, 2f]);
		var (root, sprite) = Scene(shape);
		var compositor = new DrawListCompositor();
		var frame = 0;

		void Frame()
		{
			frame++;
			sprite.Transform = Matrix3x2.CreateTranslation(frame % 7, frame % 5);
			sprite.Opacity = 0.5f + (frame % 3) * 0.1f;
			paint.Color = new HdrColor(1f, (frame % 10) / 10f, 0f, 0.8f);
			shape.Trim = new PathTrim(0f, 0.3f + (frame % 6) / 10f, (frame % 4) / 4f);
			Composite(compositor, root);
		}

		for (var i = 0; i < 20; i++)
			Frame();
		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 30; i++)
				Frame();
		});
	}
}
