using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// The geometry model: each geometry draws the path it describes with tight bounds; a change anywhere in a geometry
/// reaches its listeners once and rebuilds its <see cref="CompositionPath"/> in place; WinUI's one-parent rule holds for
/// figures and segments; and <see cref="PointCollection.SetAll"/> updates a path without allocating.
/// </summary>
public sealed class GeometryTests
{
	private sealed class CountingListener : ISubPropertyListener
	{
		public int Changes { get; private set; }

		public void OnSubPropertyChanged(object source) => Changes++;
	}

	private static (PathGeometry Geometry, LineSegment Line, CountingListener Listener) Watched()
	{
		var line = new LineSegment { Point = new Point(10, 0) };
		var geometry = new PathGeometry { Figures = { new PathFigure { StartPoint = new Point(0, 0), Segments = { line } } } };
		var listener = new CountingListener();
		geometry.AddListener(listener);
		return (geometry, line, listener);
	}

	[Test]
	public void Each_geometry_draws_its_shape()
	{
		new RectangleGeometry { Rect = new Rect(1, 2, 3, 4) }.CompositionPath.ToString().ShouldBe("M 1,2 L 4,2 L 4,6 L 1,6 Z");
		new LineGeometry { StartPoint = new Point(1, 2), EndPoint = new Point(3, 4) }.CompositionPath.ToString().ShouldBe("M 1,2 L 3,4");
		new RectangleGeometry().CompositionPath.IsEmpty.ShouldBeTrue();
		new EllipseGeometry { RadiusX = 0, RadiusY = 5 }.CompositionPath.IsEmpty.ShouldBeTrue();
	}

	[Test]
	public void A_line_geometry_encloses_nothing_and_a_rectangle_does()
	{
		new LineGeometry { EndPoint = new Point(3, 4) }.CompositionPath.GetFigure(0).IsFilled.ShouldBeFalse();
		var rect = new RectangleGeometry { Rect = new Rect(0, 0, 1, 1) }.CompositionPath.GetFigure(0);
		(rect.IsFilled, rect.IsClosed, rect.VerbCount, rect.PointCount).ShouldBe((true, true, 5, 4));
	}

	[Test]
	public void An_ellipse_lies_on_its_ellipse_within_a_millionth_of_its_radius()
	{
		var ellipse = new EllipseGeometry { Center = new Point(50, 40), RadiusX = 200, RadiusY = 100 };
		var path = ellipse.CompositionPath;

		path.Verbs.ToArray().Count(v => v == PathVerb.Cubic).ShouldBe(8);
		ellipse.Bounds.ShouldBeNear(new Rect(-150, -60, 400, 200), 1e-3f);
		foreach (var p in CubicSamples(path))
		{
			var d = new Vector2((p.X - 50) / 200, (p.Y - 40) / 100).Length();
			MathF.Abs(d - 1).ShouldBeLessThan(1e-5f);
		}
	}

	[Test]
	public void Bounds_follow_a_curve_not_its_control_points()
	{
		Geometry.Parse("M 0,0 C 0,10 10,10 10,0").Bounds.ShouldBeNear(new Rect(0, 0, 10, 7.5f), 1e-4f);
		Geometry.Parse("M 0,0 Q 5,10 10,0").Bounds.ShouldBeNear(new Rect(0, 0, 10, 5), 1e-4f);
		new PathGeometry().Bounds.ShouldBe(Rect.Zero);
	}

	[Test]
	[Arguments("M 0,0 A 1,1 0 0 1 10,0", 5f)]
	[Arguments("M 0,0 A 1,1 0 1 1 10,0", 5f)]
	public void Radii_too_small_to_reach_the_end_grow_until_they_do(string markup, float radius)
	{
		foreach (var p in CubicSamples(Geometry.Parse(markup).CompositionPath))
			Vector2.Distance(p, new Vector2(5, 0)).ShouldBe(radius, 1e-4f);
	}

	[Test]
	public void A_zero_radius_arc_is_a_line_and_an_arc_to_its_start_draws_nothing()
	{
		Geometry.Parse("M 0,0 A 0,4 0 0 1 10,0").CompositionPath.ToString().ShouldBe("M 0,0 L 10,0");
		Geometry.Parse("M 3,3 A 4,4 0 0 1 3,3").CompositionPath.ToString().ShouldBe("M 3,3");
	}

	[Test]
	public void A_rotated_arc_keeps_its_ellipse()
	{
		// A 30°-rotated ellipse with radii 4 and 2 through two of its points: every sample satisfies its equation.
		var path = Geometry.Parse("M 0,0 A 4,2 30 1 1 3,1").CompositionPath;
		var samples = CubicSamples(path).ToArray();
		var center = FitCenter(Vector2.Zero, new Vector2(3, 1), 4, 2, 30, large: true, clockwise: true);
		samples[^1].ShouldBeNear(new Vector2(3, 1), 1e-6f);
		var (c, s) = (MathF.Cos(MathF.PI / 6), MathF.Sin(MathF.PI / 6));
		foreach (var p in samples)
		{
			var d = p - center;
			var (u, v) = (c * d.X + s * d.Y, -s * d.X + c * d.Y);
			(u * u / 16 + v * v / 4).ShouldBe(1f, 1e-3f);
		}
	}

	[Test]
	public void A_change_deep_in_a_geometry_reaches_its_listener_once_and_rebuilds_the_path()
	{
		var (geometry, line, listener) = Watched();
		var before = geometry.CompositionPath.Version;

		line.Point = new Point(20, 5);

		listener.Changes.ShouldBe(1);
		geometry.CompositionPath.ToString().ShouldBe("M 0,0 L 20,5");
		geometry.CompositionPath.Version.ShouldNotBe(before);
	}

	[Test]
	public void Structural_changes_reach_the_listener_and_a_removed_part_stops_reporting()
	{
		var (geometry, line, listener) = Watched();
		var figure = geometry.Figures[0];

		figure.IsClosed = true;
		figure.Segments.Add(new LineSegment { Point = new Point(5, 5) });
		figure.Segments.Remove(line);
		listener.Changes.ShouldBe(3);
		geometry.CompositionPath.ToString().ShouldBe("M 0,0 L 5,5 Z");

		line.Point = new Point(99, 99);
		listener.Changes.ShouldBe(3);

		geometry.Figures.Clear();
		listener.Changes.ShouldBe(4);
		geometry.CompositionPath.IsEmpty.ShouldBeTrue();
	}

	[Test]
	public void Replacing_a_collection_listens_to_the_new_one_only()
	{
		var segment = new PolyLineSegment();
		var first = segment.Points; // created on first use
		var geometry = new PathGeometry { Figures = { new PathFigure { Segments = { segment } } } };
		var listener = new CountingListener();
		geometry.AddListener(listener);

		var replacement = new PointCollection { new Point(1, 1) };
		segment.Points = replacement;
		first.Add(new Point(7, 7));
		replacement.Add(new Point(2, 2));

		listener.Changes.ShouldBe(2);
		geometry.CompositionPath.ToString().ShouldBe("M 0,0 L 1,1 L 2,2");
	}

	[Test]
	public void A_group_reports_its_childrens_changes_and_fills_by_its_own_rule()
	{
		var rect = new RectangleGeometry { Rect = new Rect(0, 0, 4, 4) };
		var line = new LineGeometry { EndPoint = new Point(1, 1) };
		var group = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { rect, line } };
		var listener = new CountingListener();
		group.AddListener(listener);

		rect.Rect = new Rect(0, 0, 2, 2);

		listener.Changes.ShouldBe(1);
		var path = group.CompositionPath;
		path.FillRule.ShouldBe(FillRule.Nonzero);
		path.ToString().ShouldBe("F1 M 0,0 L 2,0 L 2,2 L 0,2 Z M 0,0 L 1,1");
		path.GetFigure(1).IsFilled.ShouldBeFalse();

		group.Children.Remove(rect);
		rect.Rect = new Rect(0, 0, 9, 9);
		listener.Changes.ShouldBe(2);
		rect.ListenerCount.ShouldBe(0);
	}

	[Test]
	public void A_figure_or_segment_belongs_to_one_collection_at_a_time()
	{
		var segment = new LineSegment();
		var figure = new PathFigure { Segments = { segment } };
		_ = new PathGeometry { Figures = { figure } };

		Should.Throw<InvalidOperationException>(() => new PathFigure().Segments.Add(segment));
		Should.Throw<InvalidOperationException>(() => new PathGeometry().Figures.Add(figure));

		figure.Segments.Clear();
		new PathFigure().Segments.Add(segment); // free again once removed
	}

	[Test]
	public void An_unfilled_figure_is_marked_in_the_path()
	{
		var geometry = new PathGeometry
		{
			Figures =
			{
				new PathFigure { IsFilled = false, Segments = { new LineSegment { Point = new Point(1, 0) } } },
				new PathFigure { Segments = { new LineSegment { Point = new Point(0, 1) } } },
			},
		};
		var path = geometry.CompositionPath;
		path.GetFigure(0).IsFilled.ShouldBeFalse();
		path.GetFigure(1).IsFilled.ShouldBeTrue();
	}

	[Test]
	public void SetAll_replaces_the_points_with_one_change_and_no_allocation_once_warm()
	{
		var segment = new PolyLineSegment { Points = [] };
		var geometry = new PathGeometry { Figures = { new PathFigure { Segments = { segment } } } };
		var listener = new CountingListener();
		geometry.AddListener(listener);
		var a = Enumerable.Range(0, 500).Select(i => new Point(i, i % 7)).ToArray();
		var b = Enumerable.Range(0, 500).Select(i => new Point(i, -(i % 5))).ToArray();

		// Warm: grow every retained buffer to this size.
		for (var i = 0; i < 4; i++)
		{
			segment.Points.SetAll(i % 2 == 0 ? a : b);
			_ = geometry.CompositionPath.Bounds;
		}
		listener.Changes.ShouldBe(4);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++)
		{
			segment.Points.SetAll(i % 2 == 0 ? a : b);
			_ = geometry.CompositionPath.Bounds;
		}
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0);
		listener.Changes.ShouldBe(104);
		geometry.CompositionPath.Points.Length.ShouldBe(501);
	}

	[Test]
	public void A_path_draws_only_inside_a_figure()
	{
		var path = new CompositionPath();
		Should.Throw<InvalidOperationException>(() => path.LineTo(Vector2.One));
		path.MoveTo(Vector2.Zero);
		path.Close();
		Should.Throw<InvalidOperationException>(() => path.LineTo(Vector2.One));
	}

	// Points along every cubic of a path, past its start.
	private static IEnumerable<Vector2> CubicSamples(CompositionPath path)
	{
		var verbs = path.Verbs.ToArray();
		var points = path.Points.ToArray();
		var p = 0;
		var last = Vector2.Zero;
		foreach (var verb in verbs)
		{
			switch (verb)
			{
				case PathVerb.Move or PathVerb.Line:
					last = points[p++];
					break;
				case PathVerb.Cubic:
					var (c1, c2, e) = (points[p], points[p + 1], points[p + 2]);
					p += 3;
					for (var t = 0.1f; t <= 1.001f; t += 0.1f)
					{
						var u = 1 - t;
						yield return u * u * u * last + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * e;
					}
					last = e;
					break;
			}
		}
	}

	// The centre of the rotated ellipse through `a` and `b` that an arc with these flags uses (SVG's F.6.5, no growth).
	private static Vector2 FitCenter(Vector2 a, Vector2 b, float rx, float ry, float degrees, bool large, bool clockwise)
	{
		var phi = degrees * MathF.PI / 180;
		var (c, s) = (MathF.Cos(phi), MathF.Sin(phi));
		var h = (a - b) / 2;
		var (x1, y1) = (c * h.X + s * h.Y, -s * h.X + c * h.Y);
		var num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
		var den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
		var coef = MathF.Sqrt(MathF.Max(0, num / den)) * (large == clockwise ? -1 : 1);
		var (cxp, cyp) = (coef * rx * y1 / ry, -coef * ry * x1 / rx);
		return new Vector2(c * cxp - s * cyp, s * cxp + c * cyp) + (a + b) / 2;
	}
}
