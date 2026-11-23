using System.Numerics;
using Vysara.Composition;
using Vysara.Drawing;
using Vysara.Media;

namespace Vysara.Tests;

/// <summary>
/// The tessellator's invariants: fills are watertight and fill what their rule says with holes where figures nest;
/// every antialiased edge carries its true signed distance, half a pixel either side; strokes have WinUI's joins, caps
/// and dash lengths, and every vertex its arc length; and re-tessellating allocates nothing once warm. Fills of
/// crossing figures are in <see cref="SweepFillTests"/>.
/// </summary>
public sealed class TessellatorTests
{
	private const float M = 0.5f; // half a pixel at scale 1

	private static TessellatedPath Fill(string markup)
	{
		var output = new TessellatedPath();
		new Tessellator().Fill(Geometry.Parse(markup).CompositionPath, TessellationScale.Identity, output);
		return output;
	}

	private static TessellatedPath Stroke(string markup, Action<CompositionStrokeStyle> style)
	{
		var s = new CompositionStrokeStyle();
		style(s);
		var output = new TessellatedPath();
		new Tessellator().Stroke(Geometry.Parse(markup).CompositionPath, s, TessellationScale.Identity, output);
		return output;
	}

	private static float Area(VectorVertex a, VectorVertex b, VectorVertex c) =>
		MathF.Abs((b.Position.X - a.Position.X) * (c.Position.Y - a.Position.Y) - (c.Position.X - a.Position.X) * (b.Position.Y - a.Position.Y)) / 2f;

	private static float TotalArea(ReadOnlySpan<VectorVertex> v, Func<VectorVertex, bool>? where = null)
	{
		var total = 0f;
		for (var i = 0; i < v.Length; i += 3)
		{
			if (where is null || where(v[i]))
				total += Area(v[i], v[i + 1], v[i + 2]);
		}
		return total;
	}

	private static bool IsInterior(VectorVertex v) => v.Distances == VectorVertex.NoEdges;

	// Whether point p is inside any triangle of the interior (not the fringe).
	private static bool InteriorCovers(TessellatedPath path, Vector2 p)
	{
		var v = path.Vertices;
		for (var i = 0; i < v.Length; i += 3)
		{
			if (!IsInterior(v[i]))
				continue;
			if (Inside(v[i].Position, v[i + 1].Position, v[i + 2].Position, p))
				return true;
		}
		return false;
	}

	private static bool Inside(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
	{
		static float Cross(Vector2 o, Vector2 u, Vector2 w) => (u.X - o.X) * (w.Y - o.Y) - (u.Y - o.Y) * (w.X - o.X);
		float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
		return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
	}

	// --- Fills ---

	[Test]
	public void A_square_fill_is_its_inset_interior_plus_a_one_pixel_fringe_with_no_gap_or_overlap()
	{
		var path = Fill("M 0,0 L 10,0 10,10 0,10 Z");
		var v = path.Vertices;

		TotalArea(v, IsInterior).ShouldBe(9f * 9f, 1e-3f);           // inset by half a pixel each side
		TotalArea(v).ShouldBe(11f * 11f, 1e-3f);                      // plus the fringe out to half a pixel outside
		path.Bounds.ShouldBe(new Rect(-M, -M, 10 + 2 * M, 10 + 2 * M));
		path.IsStroke.ShouldBeFalse();
	}

	[Test]
	public void A_curved_fill_is_watertight()
	{
		var path = Fill("M 50,10 A 40,40 0 1 1 50,90 A 40,40 0 1 1 50,10 Z");
		var v = path.Vertices;

		// The fringe's outer and inner edges, in order: each edge's quad is (in A, in B, out B), (in A, out B, out A).
		var outer = new List<Vector2>();
		var inner = new List<Vector2>();
		var fringe = 0;
		for (var i = 0; i < v.Length; i += 3)
		{
			if (IsInterior(v[i]) || fringe++ % 2 == 0)
				continue;
			inner.Add(v[i].Position);
			outer.Add(v[i + 2].Position);
		}

		// Interior and fringe tile the outset polygon exactly, and the interior fills the inset one: no gap, no overlap.
		TotalArea(v).ShouldBe(PolygonArea(outer), PolygonArea(outer) * 1e-5f);
		TotalArea(v, IsInterior).ShouldBe(PolygonArea(inner), PolygonArea(inner) * 1e-5f);
		// And the outline is the circle's, within the flattening tolerance.
		foreach (var p in outer)
			Vector2.Distance(p, new Vector2(50, 50)).ShouldBe(40.5f, 0.3f);
	}

	private static float PolygonArea(List<Vector2> ring)
	{
		var area = 0f;
		for (var i = 0; i < ring.Count; i++)
		{
			var (a, b) = (ring[i], ring[(i + 1) % ring.Count]);
			area += a.X * b.Y - b.X * a.Y;
		}
		return MathF.Abs(area) / 2f;
	}

	[Test]
	public void Every_fringe_vertex_carries_its_true_distance_to_the_edge()
	{
		var path = Fill("M 0,0 L 10,0 10,10 Z");
		var vertices = path.Vertices.ToArray();
		var edges = new (Vector2 A, Vector2 B)[] { (new(0, 0), new(10, 0)), (new(10, 0), new(10, 10)), (new(10, 10), new(0, 0)) };
		var fringe = 0;
		foreach (var vertex in vertices)
		{
			if (IsInterior(vertex))
				continue;
			fringe++;
			var d = vertex.Distances;
			(d.Y, d.Z, d.W).ShouldBe((VectorVertex.Unused, VectorVertex.Unused, VectorVertex.Unused));
			// Inside vertices are half a pixel in from their edge's line, outside ones half a pixel out.
			MathF.Abs(MathF.Abs(d.X) - M).ShouldBeLessThan(1e-4f);
			edges.Any(e => MathF.Abs(SignedDistance(e.A, e.B, vertex.Position) - d.X) < 1e-4f).ShouldBeTrue();
		}
		fringe.ShouldBe(3 * 6);
	}

	// Positive on the filled side of a contour that turns the way a positive-area ring does.
	private static float SignedDistance(Vector2 a, Vector2 b, Vector2 p)
	{
		var t = Vector2.Normalize(b - a);
		return Vector2.Dot(p - a, new Vector2(-t.Y, t.X));
	}

	[Test]
	[Arguments("M 0,0 L 30,0 30,30 0,30 Z M 10,10 L 20,10 20,20 10,20 Z", false)]       // even-odd, same direction: a hole
	[Arguments("F1 M 0,0 L 30,0 30,30 0,30 Z M 10,10 L 20,10 20,20 10,20 Z", true)]     // nonzero, same direction: filled
	[Arguments("F1 M 0,0 L 30,0 30,30 0,30 Z M 10,10 L 10,20 20,20 20,10 Z", false)]    // nonzero, opposite: a hole
	public void Nested_figures_fill_by_the_rule(string markup, bool centreFilled)
	{
		var path = Fill(markup);

		InteriorCovers(path, new Vector2(15, 15)).ShouldBe(centreFilled);
		InteriorCovers(path, new Vector2(5, 15)).ShouldBeTrue();
		InteriorCovers(path, new Vector2(35, 15)).ShouldBeFalse();
		var expected = centreFilled ? 29f * 29f : 29f * 29f - 11f * 11f;
		TotalArea(path.Vertices, IsInterior).ShouldBe(expected, 1e-2f);
	}

	[Test]
	public void An_island_in_a_hole_is_filled_again()
	{
		var path = Fill("M 0,0 L 50,0 50,50 0,50 Z M 10,10 L 40,10 40,40 10,40 Z M 20,20 L 30,20 30,30 20,30 Z");

		InteriorCovers(path, new Vector2(5, 5)).ShouldBeTrue();
		InteriorCovers(path, new Vector2(15, 15)).ShouldBeFalse();
		InteriorCovers(path, new Vector2(25, 25)).ShouldBeTrue();
	}

	[Test]
	public void A_concave_fill_covers_its_shape_and_not_its_notch()
	{
		var path = Fill("M 0,0 L 30,0 30,30 20,30 20,10 10,10 10,30 0,30 Z");

		InteriorCovers(path, new Vector2(5, 20)).ShouldBeTrue();
		InteriorCovers(path, new Vector2(25, 20)).ShouldBeTrue();
		InteriorCovers(path, new Vector2(15, 20)).ShouldBeFalse();
		// The shape (700) outset by half a pixel: its perimeter (160) times the margin, plus a margin squared at each of six
		// convex corners, less one at each of two reflex ones.
		TotalArea(path.Vertices).ShouldBe(700f + 160f * M + (6 - 2) * M * M, 1e-2f);
	}

	[Test]
	public void Unfilled_and_arealess_figures_draw_no_fill()
	{
		new Tessellator().Fill(new LineGeometry { EndPoint = new Point(10, 10) }.CompositionPath, TessellationScale.Identity, new TessellatedPath());
		Fill("M 0,0 L 10,0 L 20,0 Z").TriangleCount.ShouldBe(0);
		var unfilled = new PathGeometry
		{
			Figures = { new PathFigure { IsFilled = false, Segments = { new PolyLineSegment { Points = [new Point(10, 0), new Point(10, 10)] } } } },
		};
		var output = new TessellatedPath();
		new Tessellator().Fill(unfilled.CompositionPath, TessellationScale.Identity, output);
		output.TriangleCount.ShouldBe(0);
	}

	[Test]
	public void An_open_figure_fills_as_if_closed()
	{
		TotalArea(Fill("M 0,0 L 10,0 10,10 0,10").Vertices).ShouldBe(11f * 11f, 1e-3f);
	}

	// --- Strokes ---

	[Test]
	[Arguments(CompositionStrokeCap.Flat, 0f)]
	[Arguments(CompositionStrokeCap.Square, 5f)]
	[Arguments(CompositionStrokeCap.Round, 5f)]
	[Arguments(CompositionStrokeCap.Triangle, 5f)]
	public void A_cap_reaches_its_length_past_the_end_plus_the_margin(CompositionStrokeCap cap, float reach)
	{
		var path = Stroke("M 0,0 L 100,0", s => { s.Thickness = 10; s.StartCap = cap; s.EndCap = cap; });
		var b = path.Bounds;

		// A triangle cap antialiases 45° edges, whose margin reaches √2 half pixels along both axes. A round cap is a
		// polygon of tangents, whose corners stand out from the circle by up to the flattening tolerance's angle.
		var margin = cap == CompositionStrokeCap.Triangle ? MathF.Sqrt(2f) * M : M;
		var slack = cap == CompositionStrokeCap.Round ? 0.25f : 0.01f;
		b.Right.ShouldBeInRange(100 + reach + margin - 0.01f, 100 + reach + margin + slack);
		b.X.ShouldBeInRange(-reach - margin - slack, -reach - margin + 0.01f);
		var side = cap == CompositionStrokeCap.Triangle ? 5 + margin : 5 + M;
		b.Y.ShouldBeInRange(-side - slack, -side + 0.01f);
		b.Bottom.ShouldBeInRange(side - 0.01f, side + slack);
		path.Length.ShouldBe(100f);
		path.SegmentCount.ShouldBe(1);
	}

	[Test]
	public void A_body_carries_its_two_sides_and_a_flat_cap_its_end_line()
	{
		var v = Stroke("M 0,0 L 100,0", s => s.Thickness = 10).Vertices;

		foreach (var vertex in v)
		{
			var p = vertex.Position;
			var d = vertex.Distances;
			// The sides are y = ±5: distance to each, positive inside.
			d.X.ShouldBe(5 - p.Y, 1e-4f);
			d.Y.ShouldBe(5 + p.Y, 1e-4f);
			// Flat caps clip at x = 0 and x = 100 where they apply.
			if (d.Z != VectorVertex.Unused)
				d.Z.ShouldBe(p.X, 1e-4f);
			if (d.W != VectorVertex.Unused)
				d.W.ShouldBe(100 - p.X, 1e-4f);
		}
	}

	[Test]
	[Arguments(CompositionStrokeLineJoin.Miter, 5.5f * 1.41421356f)]   // the miter tip, w / cos 45°
	[Arguments(CompositionStrokeLineJoin.Bevel, 5f * 0.70710678f + M)] // the bevel line, h cos 45°, plus the margin
	[Arguments(CompositionStrokeLineJoin.Round, 5.5f)]                 // the round join's tangent polygon, at h plus the margin
	public void A_right_angle_join_reaches_as_its_kind_says(CompositionStrokeLineJoin join, float reach)
	{
		var path = Stroke("M 0,0 L 50,0 L 50,50", s => { s.Thickness = 10; s.LineJoin = join; });

		// The outer corner is towards (+x, -y) from (50, 0); measure the reach along that diagonal.
		var diagonal = Vector2.Normalize(new Vector2(1, -1));
		var furthest = path.Vertices.ToArray().Max(v => Vector2.Dot(v.Position - new Vector2(50, 0), diagonal));
		furthest.ShouldBe(reach, join == CompositionStrokeLineJoin.Round ? 0.3f : 1e-3f);
		path.SegmentCount.ShouldBe(2);
	}

	[Test]
	public void Neighbouring_wedges_of_a_round_fan_share_their_spoke_exactly()
	{
		// Each wedge's last rim point is the next wedge's first. Computed once per wedge, the two copies rounded apart and
		// left a hairline gap along the spoke: Loom's GPU, snapping vertices to 1/256 pixel, drew over it; the oracle did not.
		var path = Stroke("M 480,190 L 560,70", s => { s.Thickness = 18; s.StartCap = CompositionStrokeCap.Round; });
		var v = path.Vertices.ToArray();
		var wedges = Enumerable.Range(0, v.Length / 3).Where(t => v[3 * t].Position == new Vector2(480, 190)).ToArray();

		wedges.Length.ShouldBeGreaterThan(2);
		for (var i = 1; i < wedges.Length; i++)
			v[3 * wedges[i] + 1].Position.ShouldBe(v[3 * wedges[i - 1] + 2].Position, $"spoke {i}");
	}

	[Test]
	public void A_miter_past_its_limit_is_cut_off_there()
	{
		// A 10° turn back: uncut, the miter would reach about eleven half thicknesses past the corner, along its bisector.
		var path = Stroke("M 0,0 L 50,0 L 0,8.8", s => { s.Thickness = 2; s.MiterLimit = 4; });
		var corner = new Vector2(50, 0);
		var bisector = Vector2.Normalize(Vector2.UnitX - Vector2.Normalize(new Vector2(-50, 8.8f)));

		var tip = path.Vertices.ToArray().Max(v => Vector2.Dot(v.Position - corner, bisector));
		tip.ShouldBe(4 * 1 + M, 1e-3f); // cut at four half thicknesses, plus the margin
		path.Vertices.ToArray().Any(v => v.Distances.Y != VectorVertex.Unused && v.Distances.Y < 0).ShouldBeTrue(); // the cut line
	}

	[Test]
	public void Every_vertex_carries_its_arc_length_along_the_whole_geometry()
	{
		var line = Stroke("M 0,0 L 100,0", s => { s.Thickness = 2; s.StartCap = s.EndCap = CompositionStrokeCap.Round; });
		foreach (var v in line.Vertices)
			v.ArcLength.ShouldBe(Math.Clamp(v.Position.X, 0, 100), 1e-4f);

		// A second figure continues from the first's length.
		var path = Stroke("M 0,0 L 10,0 L 10,10 M 100,0 L 100,30", s => s.Thickness = 2);
		path.Length.ShouldBe(50f);
		foreach (var v in path.Vertices)
		{
			if (v.Position.X > 50)
				v.ArcLength.ShouldBe(20 + Math.Clamp(v.Position.Y, 0, 30), 1e-4f);
			else
				v.ArcLength.ShouldBeInRange(0f, 20f);
		}
	}

	/// <summary>
	/// A stroke's pieces overlap where they meet, so a GPU cannot crack them apart. A join or a round or triangle cap
	/// holds only half of the cross-section where it meets a segment's body (its spokes start at the centreline), so the
	/// two meet at a T-junction; abutting there, a GPU's vertex snapping can open a hairline that a pixel centre falls
	/// through, as Loom's GPU run of a curved trail showed. So every point of every body's ends inside the stroke must lie
	/// inside some triangle by half of <see cref="StrokePrimitives.SeamOverlap"/>, several times what snapping moves a
	/// vertex. A polyline turning both ways and a curve (whose flattened points are joined too) cover every join and cap.
	/// </summary>
	[Test]
	[Arguments(CompositionStrokeLineJoin.Miter, CompositionStrokeCap.Round)]
	[Arguments(CompositionStrokeLineJoin.Bevel, CompositionStrokeCap.Triangle)]
	[Arguments(CompositionStrokeLineJoin.Round, CompositionStrokeCap.Round)]
	public void Pieces_overlap_where_they_meet_so_no_seam_can_open(CompositionStrokeLineJoin join, CompositionStrokeCap cap)
	{
		var path = Stroke("M 10,80 L 60,20 L 110,80 L 160,30 C 200,0 240,60 280,30",
			s => { s.Thickness = 9; s.LineJoin = join; s.StartCap = s.EndCap = cap; });
		var triangles = path.Vertices.ToArray();
		var h = path.HalfThickness;

		var probed = 0;
		var shallowest = float.PositiveInfinity;
		foreach (var segment in path.Segments.Span.ToArray())
		{
			var normal = new Vector2(-segment.Direction.Y, segment.Direction.X);
			foreach (var end in new[] { segment.Start, segment.Start + segment.Length * segment.Direction })
			{
				// Across the end's cross-section, inside the stroke and clear of its edges' ramps.
				for (var s = -(h - M); s <= h - M; s += 0.25f)
				{
					var depth = DeepestInside(triangles, end + s * normal);
					shallowest = MathF.Min(shallowest, depth);
					probed++;
				}
			}
		}

		probed.ShouldBeGreaterThan(100);
		shallowest.ShouldBeGreaterThanOrEqualTo(StrokePrimitives.SeamOverlap / 2f,
			$"a point where two pieces meet is only {shallowest} px inside any triangle");
	}

	// How far inside the triangle that holds `p` most deeply it is: the least distance to that triangle's edges. Negative
	// when no triangle holds it.
	private static float DeepestInside(VectorVertex[] triangles, Vector2 p)
	{
		var deepest = float.NegativeInfinity;
		for (var i = 0; i < triangles.Length; i += 3)
		{
			var a = triangles[i].Position;
			var b = triangles[i + 1].Position;
			var c = triangles[i + 2].Position;
			var depth = MathF.Min(MathF.Min(Inside(a, b, c, p), Inside(b, c, a, p)), Inside(c, a, b, p));
			deepest = MathF.Max(deepest, depth);
		}
		return deepest;

		// The distance of `p` from the line through `e0` and `e1`, positive on the side `other` is on.
		static float Inside(Vector2 e0, Vector2 e1, Vector2 other, Vector2 p)
		{
			var edge = e1 - e0;
			var length = edge.Length();
			if (length < 1e-6f)
				return float.NegativeInfinity;
			var n = new Vector2(-edge.Y, edge.X) / length;
			var side = MathF.Sign(Vector2.Dot(other - e0, n));
			return side * Vector2.Dot(p - e0, n);
		}
	}

	[Test]
	[Arguments(new float[] { 2, 1 }, 0f, new float[] { 0, 4, 6, 10, 12, 16, 18, 22, 24, 28 })]
	[Arguments(new float[] { 2, 1 }, 1f, new float[] { 0, 2, 4, 8, 10, 14, 16, 20, 22, 26, 28, 30 })]
	[Arguments(new float[] { 1 }, 0f, new float[] { 0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30 })]
	[Arguments(new float[] { 2, 0, 1, 1 }, 0f, new float[] { 0, 6, 8, 14, 16, 22, 24, 30 })]
	public void Dashes_are_in_thicknesses_and_start_their_offset_into_the_pattern(float[] dashes, float offset, float[] expected)
	{
		var path = Stroke("M 0,0 L 30,0", s => { s.Thickness = 2; s.SetDashArray(dashes); s.DashOffset = offset; });

		var runs = path.Runs.Span.ToArray().Select(r => (r.R0, r.R1)).ToArray();
		runs.SelectMany(r => new[] { r.R0, r.R1 }).ShouldBe(expected);
		path.Length.ShouldBe(30f);
	}

	[Test]
	public void Dash_ends_take_the_dash_cap_and_figure_ends_the_line_caps()
	{
		var path = Stroke("M 0,0 L 30,0", s =>
		{
			s.Thickness = 2;
			s.SetDashArray([2, 1]);
			s.StartCap = CompositionStrokeCap.Round;
			s.EndCap = CompositionStrokeCap.Triangle;
			s.DashCap = CompositionStrokeCap.Square;
		});
		var runs = path.Runs.Span.ToArray();

		(runs[0].StartCap, runs[0].EndCap).ShouldBe((CompositionStrokeCap.Round, CompositionStrokeCap.Square));
		(runs[^1].StartCap, runs[^1].EndCap).ShouldBe((CompositionStrokeCap.Square, CompositionStrokeCap.Square));
	}

	[Test]
	public void On_a_closed_figure_a_dash_crossing_its_start_is_one_dash_through_the_closing_join()
	{
		// A 40-long square with dashes of 6 on, 4 off, starting 2 into a dash: the last dash runs from 38 round to 4.
		var path = Stroke("M 0,0 L 10,0 10,10 0,10 Z", s => { s.Thickness = 1; s.SetDashArray([6, 4]); s.DashOffset = 2; });
		var runs = path.Runs.Span.ToArray();

		runs.ShouldContain(r => r.R0 == 38 && r.R1 == 44);
		runs.Length.ShouldBe(4);
		runs.ShouldAllBe(r => !r.Loop);
		path.Pieces.Span.ToArray().Count(p => p.Kind == TessellatedPath.PieceKind.Join).ShouldBe(4); // one per corner, the start's inside the wrapped dash
	}

	[Test]
	public void A_dot_draws_its_caps_back_to_back_and_a_flat_dot_nothing()
	{
		Stroke("M 5,5 L 5,5", s => { s.Thickness = 4; s.StartCap = s.EndCap = CompositionStrokeCap.Flat; }).TriangleCount.ShouldBe(0);
		var round = Stroke("M 5,5 L 5,5", s => { s.Thickness = 4; s.StartCap = s.EndCap = CompositionStrokeCap.Round; });
		round.Bounds.ShouldBeNear(new Rect(5 - 2.5f, 5 - 2.5f, 5, 5), 0.25f);
		var square = Stroke("M 5,5 L 5,5", s => { s.Thickness = 4; s.StartCap = s.EndCap = CompositionStrokeCap.Square; });
		square.Bounds.ShouldBeNear(new Rect(5 - 2.5f, 5 - 2.5f, 5, 5), 1e-4f);
		Stroke("M 5,5", s => s.StartCap = CompositionStrokeCap.Round).TriangleCount.ShouldBe(0); // a lone move draws nothing
	}

	[Test]
	public void A_closed_figure_has_no_caps_and_a_closing_join()
	{
		var path = Stroke("M 0,0 L 10,0 10,10 0,10 Z", s => { s.Thickness = 2; s.StartCap = s.EndCap = CompositionStrokeCap.Round; });

		var run = path.Runs.Span.ToArray().ShouldHaveSingleItem();
		run.Loop.ShouldBeTrue();
		var kinds = path.Pieces.Span.ToArray().Select(p => p.Kind).ToArray();
		kinds.ShouldNotContain(TessellatedPath.PieceKind.StartCap);
		kinds.Count(k => k == TessellatedPath.PieceKind.ClosingJoin).ShouldBe(1);
		kinds.Count(k => k == TessellatedPath.PieceKind.Join).ShouldBe(3);
		path.Bounds.ShouldBeNear(new Rect(-1 - M, -1 - M, 12 + 2 * M, 12 + 2 * M), 1e-4f); // mitered corners
	}

	[Test]
	public void A_zero_or_negative_thickness_draws_nothing()
	{
		Stroke("M 0,0 L 10,0", s => s.Thickness = 0).TriangleCount.ShouldBe(0);
		Stroke("M 0,0 L 10,0", s => s.Thickness = -1).TriangleCount.ShouldBe(0);
	}

	/// <summary>Past the limit the rest of the stroke is drawn solid, rather than the tessellation throwing out of a
	/// frame; the path says the pattern was limited.</summary>
	[Test]
	public void A_dash_pattern_finer_than_the_limit_draws_the_rest_solid()
	{
		var path = Stroke("M 0,0 L 1000000,0", s => { s.Thickness = 0.01f; s.SetDashArray([0.5f, 0.5f]); });

		path.DashesLimited.ShouldBeTrue();
		path.Runs.Count.ShouldBe(Tessellator.MaxDashes + 1); // the dashes, then the rest as one
		path.Runs[path.Runs.Count - 1].R1.ShouldBe(path.Length);
		Stroke("M 0,0 L 100,0", s => s.SetDashArray([1f, 1f])).DashesLimited.ShouldBeFalse();
	}

	/// <summary>
	/// A dash walks the segments it covers and no more: every join it draws lies inside the dash, an open figure's dash
	/// never wraps to draw a join at the figure's start, and a dash takes at most one body per segment and one more. Float
	/// sums aren't exact off integer coordinates, which is where a walk that compared running sums went wrong.
	/// </summary>
	[Test]
	public void Dashes_on_non_integer_polylines_draw_nothing_past_their_ends()
	{
		var random = new Random(7);
		for (var trial = 0; trial < 300; trial++)
		{
			var sb = new System.Text.StringBuilder(FormattableString.Invariant($"M {random.NextSingle() * 50f},{random.NextSingle() * 50f}"));
			var points = random.Next(2, 12);
			for (var p = 0; p < points; p++)
				sb.Append(FormattableString.Invariant($" L {random.NextSingle() * 400f - 100f},{random.NextSingle() * 400f - 100f}"));
			var closed = trial % 3 == 0;
			if (closed)
				sb.Append(" Z");
			var dash = 0.37f + random.NextSingle() * 3f;
			var path = Stroke(sb.ToString(), s => { s.Thickness = 1.3f; s.SetDashArray([dash, dash * 0.71f]); });

			var pieces = path.Pieces.Span;
			var segments = path.Segments.Span;
			foreach (var run in path.Runs.Span)
			{
				var bodies = 0;
				for (var i = run.FirstPiece; i < run.FirstPiece + run.PieceCount; i++)
				{
					var piece = pieces[i];
					if (piece.Kind == TessellatedPath.PieceKind.Body)
						bodies++;
					if (piece.Kind != TessellatedPath.PieceKind.Join)
						continue;
					// A join stands at a vertex: the body before it must reach its segment's end, and the body after it
					// start the next segment, which on an open figure is never the first.
					var before = pieces[i - 1];
					var after = pieces[i + 1];
					var reached = before.X0 + (before.R1 - before.R0);
					reached.ShouldBeGreaterThan(segments[before.Segment].Length - 0.01f,
						$"trial {trial}: a join at a vertex the dash stops {segments[before.Segment].Length - reached} short of");
					if (!run.ClosedFigure)
						after.Segment.ShouldBe(before.Segment + 1, $"trial {trial}: an open figure's dash wrapped to its start");
				}
				bodies.ShouldBeLessThanOrEqualTo(run.SegmentCount + 1, $"trial {trial}");
			}
		}
	}

	[Test]
	public void Curves_are_flattened_within_a_quarter_pixel_and_finer_at_a_larger_scale()
	{
		var path = Geometry.Parse("M 0,0 C 0,100 100,100 100,0").CompositionPath;
		var style = new CompositionStrokeStyle { Thickness = 1 };
		var near = new TessellatedPath();
		var far = new TessellatedPath();
		new Tessellator().Stroke(path, style, TessellationScale.For(1f), far);
		new Tessellator().Stroke(path, style, TessellationScale.For(8f), near);

		near.SegmentCount.ShouldBeGreaterThan(far.SegmentCount * 2);
		// The flattened length is the curve's within the tolerance's effect on a chord.
		far.Length.ShouldBe(200f, 2f);
	}

	[Test]
	public void Scale_buckets_are_half_octaves_with_the_tolerance_and_margin_from_their_edges()
	{
		TessellationScale.For(1f).ShouldBe(TessellationScale.For(1.4f));
		TessellationScale.For(1f).ShouldNotBe(TessellationScale.For(1.5f));
		TessellationScale.Identity.HalfPixel.ShouldBe(0.5f);
		TessellationScale.Identity.Tolerance.ShouldBeLessThanOrEqualTo(0.25f);
		TessellationScale.For(Matrix3x2.CreateScale(4f, 1f)).ShouldBe(TessellationScale.For(Matrix3x2.CreateScale(4f, 1f) * Matrix3x2.CreateRotation(0.3f)));
		var stretched = TessellationScale.For(Matrix3x2.CreateScale(4f, 1f));
		(stretched.FlattenBucket, stretched.MarginBucket).ShouldBe((4, 0));
	}

	[Test]
	public void Re_tessellating_a_path_of_a_steady_size_allocates_nothing()
	{
		var path = Geometry.Parse("M 0,0 C 0,100 100,100 100,0 L 50,-50 Z M 20,20 L 30,20 30,30 Z").CompositionPath;
		var style = new CompositionStrokeStyle { Thickness = 3, LineJoin = CompositionStrokeLineJoin.Round, StartCap = CompositionStrokeCap.Round };
		style.SetDashArray([4, 2]);
		var tessellator = new Tessellator();
		var fill = new TessellatedPath();
		var stroke = new TessellatedPath();
		for (var i = 0; i < 3; i++)
		{
			tessellator.Fill(path, TessellationScale.Identity, fill);
			tessellator.Stroke(path, style, TessellationScale.Identity, stroke);
		}

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 20; i++)
			{
				tessellator.Fill(path, TessellationScale.Identity, fill);
				tessellator.Stroke(path, style, TessellationScale.Identity, stroke);
			}
		});
		fill.IsCurrentFor(path, null, TessellationScale.Identity).ShouldBeTrue();
		stroke.IsCurrentFor(path, style, TessellationScale.Identity).ShouldBeTrue();
		style.Thickness = 4;
		stroke.IsCurrentFor(path, style, TessellationScale.Identity).ShouldBeFalse();
	}

	[Test]
	public void A_path_transform_maps_the_outline_and_not_the_pen()
	{
		var path = Geometry.Parse("M 0,0 C 0,10 10,10 10,0 L 20,5 Z").CompositionPath;
		var transform = Matrix3x2.CreateScale(3f, 2f) * Matrix3x2.CreateRotation(0.5f) * Matrix3x2.CreateTranslation(4f, 7f);
		var mapped = new CompositionPath();
		mapped.Append(path, transform);
		var style = new CompositionStrokeStyle { Thickness = 2, LineJoin = CompositionStrokeLineJoin.Round };
		var tessellator = new Tessellator();
		var throughTransform = new TessellatedPath();
		var premapped = new TessellatedPath();

		// The same triangles as stroking the mapped outline: the thickness stays 2, unstretched.
		tessellator.Stroke(path, transform, style, TessellationScale.Identity, throughTransform);
		tessellator.Stroke(mapped, style, TessellationScale.Identity, premapped);
		throughTransform.Vertices.ToArray().ShouldBe(premapped.Vertices.ToArray());
		throughTransform.HalfThickness.ShouldBe(1f);

		tessellator.Fill(path, transform, TessellationScale.Identity, throughTransform);
		tessellator.Fill(mapped, TessellationScale.Identity, premapped);
		throughTransform.Vertices.ToArray().ShouldBe(premapped.Vertices.ToArray());
	}

	[Test]
	public void The_cache_key_takes_in_the_path_transform_and_which_path_it_is()
	{
		var path = Geometry.Parse("M 0,0 L 10,0").CompositionPath;
		var twin = Geometry.Parse("M 0,0 L 10,0").CompositionPath;
		var style = new CompositionStrokeStyle();
		var output = new TessellatedPath();
		var moved = Matrix3x2.CreateTranslation(1f, 0f);

		new Tessellator().Stroke(path, moved, style, TessellationScale.Identity, output);

		output.IsCurrentFor(path, moved, style, TessellationScale.Identity).ShouldBeTrue();
		output.IsCurrentFor(path, style, TessellationScale.Identity).ShouldBeFalse();
		output.IsCurrentFor(path, moved, new CompositionStrokeStyle(), TessellationScale.Identity).ShouldBeFalse();
		// Another path at the same version number is not the one tessellated.
		twin.Version.ShouldBe(path.Version);
		output.IsCurrentFor(twin, moved, style, TessellationScale.Identity).ShouldBeFalse();
	}
}
