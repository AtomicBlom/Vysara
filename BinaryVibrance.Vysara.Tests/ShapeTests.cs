using System.Numerics;
using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Rasterizer;
using BinaryVibrance.Vysara.Shapes;
using Path = BinaryVibrance.Vysara.Shapes.Path;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The shapes (<see cref="Rectangle"/>, <see cref="Ellipse"/>, <see cref="Line"/>, <see cref="Polyline"/>,
/// <see cref="Polygon"/>, <see cref="Path"/>): WinUI's stroke set as dependency properties copied into a retained
/// <see cref="ShapeContent"/>; WinUI's <c>Stretch</c> layout with the pen kept inside; a change to the geometry, the points
/// or the dash array applied in place; listening only while in a live tree; hit-testing on what is drawn; and pixels
/// through the CPU oracle.
/// </summary>
public sealed class ShapeTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(255, 0, 0);
	private static readonly HdrColor Blue = HdrColor.FromRgb(0, 0, 255);

	private static UIRoot Laid(UIElement content, float w = 100f, float h = 100f)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(w, h));
		return root;
	}

	private static Canvas CanvasOf(params UIElement[] children)
	{
		var canvas = new Canvas();
		foreach (var child in children)
			canvas.Children.Add(child);
		return canvas;
	}

	private static ShapeContent Drawn(Shape shape) => shape.Visual.Content.ShouldBeOfType<ShapeContent>();

	private static Vector2 Mapped(Shape shape, float x, float y) => Vector2.Transform(new Vector2(x, y), Drawn(shape).PathTransform);

	private static SoftwareRasterizer Raster(DrawListData draw, int size = 100)
	{
		var raster = new SoftwareRasterizer(size, size);
		raster.Clear(new Vector4(0f, 0f, 0f, 1f));
		raster.Render(draw);
		return raster;
	}

	private static void ShouldBeClose(Vector4 actual, Vector4 expected, float tolerance = 2e-3f) =>
		Vector4.Distance(actual, expected).ShouldBeLessThanOrEqualTo(tolerance, $"{actual} should be within {tolerance} of {expected}");

	[Test]
	public void A_rectangle_or_an_ellipse_stretches_to_fill_by_default_and_the_others_do_not()
	{
		new Rectangle().Stretch.ShouldBe(Stretch.Fill);
		new Ellipse().Stretch.ShouldBe(Stretch.Fill);
		new Path().Stretch.ShouldBe(Stretch.None);
		new Line().Stretch.ShouldBe(Stretch.None);
		new Polyline().Stretch.ShouldBe(Stretch.None);
		new Polygon().Stretch.ShouldBe(Stretch.None);

		// The default is a default: a style's setter still applies over it.
		var styled = new Rectangle { Style = new Style { Setters = { new Setter { Property = Shape.StretchProperty, Value = Stretch.Uniform } } } };
		styled.Stretch.ShouldBe(Stretch.Uniform);
	}

	[Test]
	public void The_stroke_set_reaches_the_content_with_WinUIs_defaults()
	{
		var shape = new Line { X2 = 50f, Y2 = 10f, Stroke = new SolidColorBrush(Red) };
		Laid(shape);
		var style = Drawn(shape).StrokeStyle;
		(style.Thickness, style.MiterLimit, style.LineJoin, style.StartCap, style.EndCap, style.DashCap, style.IsDashed)
			.ShouldBe((1f, 10f, CompositionStrokeLineJoin.Miter, CompositionStrokeCap.Flat, CompositionStrokeCap.Flat, CompositionStrokeCap.Flat, false));

		shape.StrokeThickness = 3f;
		shape.StrokeLineJoin = PenLineJoin.Round;
		shape.StrokeMiterLimit = 4f;
		shape.StrokeStartLineCap = PenLineCap.Round;
		shape.StrokeEndLineCap = PenLineCap.Triangle;
		shape.StrokeDashCap = PenLineCap.Square;
		shape.StrokeDashOffset = 0.5f;
		shape.StrokeDashArray = [2, 1];
		Laid(shape);

		(style.Thickness, style.MiterLimit, style.LineJoin, style.StartCap, style.EndCap, style.DashCap, style.DashOffset)
			.ShouldBe((3f, 4f, CompositionStrokeLineJoin.Round, CompositionStrokeCap.Round, CompositionStrokeCap.Triangle, CompositionStrokeCap.Square, 0.5f));
		style.DashArray.ToArray().ShouldBe([2f, 1f]);
	}

	[Test]
	[Arguments(Stretch.Fill, 2f, 2f, 36f, 16f)]
	[Arguments(Stretch.Uniform, 2f, 2f, 16f, 16f)]
	[Arguments(Stretch.UniformToFill, 2f, 2f, 36f, 36f)]
	public void A_rectangle_fills_its_space_with_its_stroke_inside(Stretch stretch, float x, float y, float w, float h)
	{
		var rect = new Rectangle { Width = 40f, Height = 20f, Stretch = stretch, Stroke = new SolidColorBrush(Red), StrokeThickness = 4f };
		var canvas = new Canvas();
		canvas.Children.Add(rect);
		Laid(canvas);

		Drawn(rect).Path!.Bounds.ShouldBe(new Rect(x, y, w, h));
	}

	[Test]
	public void A_rectangle_with_no_stretch_or_no_paint_draws_nothing()
	{
		var none = new Rectangle { Width = 40f, Height = 20f, Stretch = Stretch.None, Fill = new SolidColorBrush(Red) };
		var unpainted = new Rectangle { Width = 40f, Height = 20f };
		var panel = new StackPanel();
		panel.Children.Add(none);
		panel.Children.Add(unpainted);
		Laid(panel);

		none.Visual.Content.ShouldBeNull();
		unpainted.Visual.Content.ShouldBeNull();
	}

	[Test]
	public void Rounded_corners_take_each_radius_up_to_half_their_side()
	{
		var rect = new Rectangle { Width = 40f, Height = 20f, RadiusX = 5f, RadiusY = 50f, Fill = new SolidColorBrush(Red) };
		Laid(CanvasOf(rect));

		var path = Drawn(rect).Path!;
		path.ToString().ShouldStartWith("M 5,0 L 35,0 C ");
		path.Bounds.ShouldBeNear(new Rect(0, 0, 40, 20), 1e-4f);
		// The right side's straight part has no length: the radius along y is half the height.
		path.ToString().ShouldContain(" L 40,10 C ");
	}

	[Test]
	public void A_uniform_ellipse_is_the_circle_in_the_top_left_of_its_space()
	{
		var ellipse = new Ellipse { Width = 60f, Height = 40f, Stretch = Stretch.Uniform, Fill = new SolidColorBrush(Red) };
		Laid(CanvasOf(ellipse));

		Drawn(ellipse).Path!.Bounds.ShouldBeNear(new Rect(0, 0, 40, 40), 1e-3f);
	}

	[Test]
	public void With_no_stretch_a_path_asks_for_its_outline_and_pen_from_its_origin()
	{
		var path = new Path { Data = Geometry.Parse("M 10,10 L 30,10 L 30,20 Z"), Stroke = new SolidColorBrush(Red), StrokeThickness = 4f };
		path.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
		path.DesiredSize.ShouldBe(new Size(32f, 22f));

		// Without a stroke the pen does not count.
		path.Stroke = null;
		path.Fill = new SolidColorBrush(Red);
		path.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
		path.DesiredSize.ShouldBe(new Size(30f, 20f));
	}

	[Test]
	public void A_uniform_path_scales_its_outline_into_its_space_centred_with_the_pen_inside()
	{
		// The outline is 20×10. Into 100×100 with a pen of 2, uniformly: scale 4.9, taking 100×51, centred down the space.
		var path = new Path
		{
			Width = 100f,
			Height = 100f,
			Stretch = Stretch.Uniform,
			Data = Geometry.Parse("M 10,10 L 30,10 L 30,20 Z"),
			Stroke = new SolidColorBrush(Red),
			StrokeThickness = 2f,
		};
		Laid(CanvasOf(path));

		Mapped(path, 10f, 10f).ShouldBeNear(new Vector2(1f, 25.5f), 1e-4f);
		Mapped(path, 30f, 20f).ShouldBeNear(new Vector2(99f, 74.5f), 1e-4f);
		Drawn(path).StrokeStyle.Thickness.ShouldBe(2f); // the pen is not scaled
	}

	[Test]
	public void A_filled_path_stretches_each_way_and_a_flat_one_is_not_kept_in_proportion()
	{
		var fill = new Path { Width = 50f, Height = 30f, Stretch = Stretch.Fill, Data = Geometry.Parse("M 0,0 L 10,0 L 10,10 Z"), Fill = new SolidColorBrush(Red) };
		var flat = new Path { Width = 50f, Height = 30f, Stretch = Stretch.Uniform, Data = Geometry.Parse("M 0,5 L 10,5"), Stroke = new SolidColorBrush(Red) };
		Laid(CanvasOf(fill, flat));

		Mapped(fill, 10f, 10f).ShouldBeNear(new Vector2(50f, 30f), 1e-4f);
		// A horizontal line has no height to scale: it stretches across, and its height keeps scale 1.
		Mapped(flat, 0f, 5f).ShouldBeNear(new Vector2(0.5f, 15f), 1e-4f); // centred down the 30
		Mapped(flat, 10f, 5f).ShouldBeNear(new Vector2(49.5f, 15f), 1e-4f);
	}

	[Test]
	public void A_paths_geometry_transform_maps_the_outline_before_it_is_stretched()
	{
		var data = Geometry.Parse("M 0,0 L 10,0 L 10,20 Z");
		data.Transform = new TranslateTransform { X = 5f, Y = 5f };
		var path = new Path { Data = data, Fill = new SolidColorBrush(Red) };
		path.Measure(new Size(100f, 100f));

		path.DesiredSize.ShouldBe(new Size(15f, 25f));
		Laid(path);
		Mapped(path, 0f, 0f).ShouldBe(new Vector2(5f, 5f));
	}

	[Test]
	public void Lines_and_polylines_are_open_and_a_polygon_closes()
	{
		var line = new Line { X1 = 1f, Y1 = 2f, X2 = 30f, Y2 = 4f, Stroke = new SolidColorBrush(Red) };
		var polyline = new Polyline { Points = { new Point(0, 0), new Point(10, 5), new Point(20, 0) }, Stroke = new SolidColorBrush(Red) };
		var polygon = new Polygon { Points = { new Point(0, 0), new Point(10, 5), new Point(20, 0) }, Fill = new SolidColorBrush(Red), FillRule = FillRule.Nonzero };
		Laid(CanvasOf(line, polyline, polygon));

		Drawn(line).Path!.ToString().ShouldBe("M 1,2 L 30,4");
		Drawn(line).Path!.GetFigure(0).IsFilled.ShouldBeFalse();
		Drawn(polyline).Path!.ToString().ShouldBe("M 0,0 L 10,5 L 20,0");
		Drawn(polygon).Path!.ToString().ShouldBe("F1 M 0,0 L 10,5 L 20,0 Z");
	}

	[Test]
	public void Changes_in_place_redraw_the_same_content()
	{
		var segment = new LineSegment { Point = new Point(10, 0) };
		var data = new PathGeometry { Figures = { new PathFigure { Segments = { segment } } } };
		var slide = new TranslateTransform();
		data.Transform = slide;
		var path = new Path { Data = data, Stroke = new SolidColorBrush(Red) };
		var polyline = new Polyline { Points = { new Point(0, 0), new Point(10, 10) }, Stroke = new SolidColorBrush(Red) };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = CanvasOf(path, polyline);
		host.Tick(0, 0f, new Vector2(100f, 100f));
		var content = Drawn(path);

		segment.Point = new Point(20, 5);
		slide.X = 3f;
		polyline.Points.SetAll([new Point(0, 0), new Point(5, 5), new Point(9, 0)]);
		path.StrokeDashArray.Add(2);
		host.Tick(0.016, 0f, new Vector2(100f, 100f));

		Drawn(path).ShouldBeSameAs(content);
		content.Path!.ToString().ShouldBe("M 0,0 L 20,5");
		content.PathTransform.ShouldBe(Matrix3x2.CreateTranslation(3f, 0f));
		content.StrokeStyle.DashArray.ToArray().ShouldBe([2f]);
		Drawn(polyline).Path!.ToString().ShouldBe("M 0,0 L 5,5 L 9,0");
		path.DesiredSize.ShouldBe(new Size(23.5f, 5.5f)); // and laid out again for the new bounds
	}

	[Test]
	public void Changes_made_while_a_shape_is_not_listening_are_drawn_when_it_joins_a_live_tree()
	{
		// Points added after the fill was set, before the polygon is in a tree, are not heard; they are drawn all the same.
		var polygon = new Polygon();
		polygon.Points.Add(new Point(0, 0));
		polygon.Fill = new SolidColorBrush(Red);
		polygon.Points.Add(new Point(20, 0));
		polygon.Points.Add(new Point(20, 10));
		var data = Geometry.Parse("M 0,0 L 10,0");
		var path = new Path { Data = data, Stroke = new SolidColorBrush(Red), StrokeDashArray = [2, 1] };
		var canvas = CanvasOf(polygon, path);
		var root = Laid(canvas);
		Drawn(polygon).Path!.ToString().ShouldBe("M 0,0 L 20,0 L 20,10 Z");
		polygon.DesiredSize.ShouldBe(new Size(20, 10));

		// Out of the tree the points, the geometry and the dashes change; back in, all of it is drawn and laid out.
		root.Content = null;
		polygon.Points.SetAll([new Point(0, 0), new Point(30, 0), new Point(30, 30)]);
		((LineSegment)data.Figures[0].Segments[0]).Point = new Point(40, 0);
		path.StrokeDashArray[0] = 4;
		root.Content = canvas;
		root.LayoutPass(new Size(100f, 100f));

		Drawn(polygon).Path!.ToString().ShouldBe("M 0,0 L 30,0 L 30,30 Z");
		polygon.DesiredSize.ShouldBe(new Size(30, 30));
		Drawn(path).Path!.ToString().ShouldBe("M 0,0 L 40,0");
		Drawn(path).StrokeStyle.DashArray.ToArray().ShouldBe([4f, 1f]);
	}

	[Test]
	public void A_shape_listens_to_its_brushes_dashes_and_outline_only_while_in_a_live_tree()
	{
		var fill = new SolidColorBrush(Red);
		var data = Geometry.Parse("M 0,0 L 10,10");
		var dashes = new DoubleCollection { 2, 1 };
		var points = new PointCollection { new Point(0, 0), new Point(5, 5) };
		var path = new Path { Data = data, Fill = fill, StrokeDashArray = dashes };
		var polyline = new Polyline { Points = points };
		var root = new UIRoot(new DrawListId(0));
		var canvas = CanvasOf(path, polyline);

		(fill.ListenerCount, data.ListenerCount, polyline.Points.Count).ShouldBe((0, 0, 2));
		root.Content = canvas;
		(fill.ListenerCount, data.ListenerCount).ShouldBe((1, 1));
		root.Content = null;
		(fill.ListenerCount, data.ListenerCount).ShouldBe((0, 0));

		// Replacing the outline while live moves the registration.
		root.Content = canvas;
		var next = Geometry.Parse("M 0,0 L 5,5");
		path.Data = next;
		(data.ListenerCount, next.ListenerCount).ShouldBe((0, 1));
	}

	[Test]
	public void A_shape_is_hit_inside_its_fill_and_on_its_stroke_only()
	{
		var filled = new Rectangle { Width = 40f, Height = 40f, Fill = new SolidColorBrush(Red) };
		var ring = new Ellipse { Width = 40f, Height = 40f, Stroke = new SolidColorBrush(Red), StrokeThickness = 4f };
		var dashed = new Line { X1 = 0f, Y1 = 10f, X2 = 40f, Y2 = 10f, Stroke = new SolidColorBrush(Red), StrokeThickness = 4f, StrokeDashArray = [1, 1] };
		var unpainted = new Rectangle { Width = 40f, Height = 40f };
		Canvas.SetLeft(ring, 50f);
		Canvas.SetTop(dashed, 50f);
		Canvas.SetLeft(unpainted, 50f);
		Canvas.SetTop(unpainted, 50f);
		var root = Laid(CanvasOf(filled, ring, dashed, unpainted));

		root.HitTest(new Vector2(20f, 20f)).ShouldBe(filled);
		root.HitTest(new Vector2(70f, 2f)).ShouldBe(ring);   // on the stroke, at the top of the circle
		root.HitTest(new Vector2(70f, 20f)).ShouldBeNull();  // its unfilled middle
		root.HitTest(new Vector2(52f, 20f)).ShouldBe(ring);  // on the stroke at its left
		root.HitTest(new Vector2(51f, 3f)).ShouldBeNull();   // the corner of its box, off the circle
		root.HitTest(new Vector2(2f, 60f)).ShouldBe(dashed);  // a dash, 0..4
		root.HitTest(new Vector2(6f, 60f)).ShouldBeNull();    // the gap, 4..8
		root.HitTest(new Vector2(2f, 64f)).ShouldBeNull();    // past half the thickness
		root.HitTest(new Vector2(70f, 70f)).ShouldBeNull();   // a shape with no paint is not hit
	}

	[Test]
	public void Shapes_draw_through_the_vector_prim_where_they_should()
	{
		var rect = new Rectangle { Width = 40f, Height = 40f, Fill = new SolidColorBrush(Red), Stroke = new SolidColorBrush(Blue), StrokeThickness = 4f };
		var draw = Laid(CanvasOf(rect)).Render();

		draw.Commands.Span.ToArray().ShouldAllBe(c => c.Tag == (uint)UIPrimKind.Vector);
		var raster = Raster(draw);
		ShouldBeClose(raster.ReadLinear(20, 20), Red.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(20, 0), Blue.ToPremultipliedVector()); // the stroke lies inside the 40×40
		ShouldBeClose(raster.ReadLinear(20, 3), Blue.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(20, 4), Red.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(41, 20), new Vector4(0f, 0f, 0f, 1f));
	}

	[Test]
	public void A_translucent_polyline_crossing_itself_blends_once()
	{
		var glass = new HdrColor(0.2f, 0.6f, 1f, 0.5f);
		var polyline = new Polyline
		{
			Points = { new Point(10, 50), new Point(90, 50), new Point(90, 10), new Point(50, 10), new Point(50, 90) },
			Stroke = new SolidColorBrush(glass),
			StrokeThickness = 6f,
		};
		var raster = Raster(Laid(CanvasOf(polyline)).Render());

		ShouldBeClose(raster.ReadLinear(50, 50), raster.ReadLinear(30, 50), 1e-5f); // blending twice would differ by a quarter
	}

	[Test]
	public void A_shapes_dump_shows_its_outline_paint_and_stroke()
	{
		var rect = new Rectangle
		{
			Width = 20f,
			Height = 10f,
			RadiusX = 2f,
			RadiusY = 2f,
			Fill = new SolidColorBrush(Red),
			Stroke = new SolidColorBrush(Blue),
			StrokeThickness = 2f,
			StrokeDashArray = [3, 1],
		};
		var line = new Line { X2 = 10f, Y2 = 10f, Stroke = new SolidColorBrush(Blue), StrokeEndLineCap = PenLineCap.Round };
		Canvas.SetTop(line, 20f);
		var canvas = CanvasOf(rect, line);
		Laid(canvas);

		rect.Visual.Dump().ShouldStartWith("SpriteVisual xform=[0,0] op=1 shape path=\"M 3,1 L 17,1 C ");
		rect.Visual.Dump().ShouldEndWith(" fill=(1,0,0,1) stroke=2(0,0,1,1) dashes=3,1\n");
		line.Visual.Dump().ShouldBe("SpriteVisual xform=[0,20] op=1 shape path=\"M 0,0 L 10,10\" stroke=1(0,0,1,1) caps=Flat,Round\n");
	}

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Animating_a_shapes_colour_transform_and_points_allocates_nothing_once_warm()
	{
		var paint = new SolidColorBrush(Red);
		var spin = new RotateTransform();
		var path = new Path
		{
			Data = Geometry.Parse("M 10,10 C 10,90 90,90 90,10 Z"),
			Fill = new SolidColorBrush(Blue),
			Stroke = paint,
			StrokeThickness = 3f,
			RenderTransform = spin,
			StrokeDashArray = [4, 2],
		};
		var line = new Polyline { Stroke = paint, StrokeThickness = 2f, Points = { new Point(0, 0), new Point(1, 1), new Point(2, 0), new Point(3, 1) } };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = CanvasOf(path, line);
		var points = new Point[4];
		var frame = 0;

		void Frame()
		{
			frame++;
			paint.Color = new HdrColor(1f, (frame % 10) / 10f, 0f, 1f);
			spin.Angle = frame % 360;
			for (var i = 0; i < points.Length; i++)
				points[i] = new Point(i * 20f, (frame + i) % 7 * 5f);
			line.Points.SetAll(points);
			host.Tick(frame * 0.016, 0.016f, new Vector2(200f, 200f));
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
