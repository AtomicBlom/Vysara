using System.Globalization;
using System.Numerics;
using Vysara.Drawing;
using Vysara.Media;
using Vysara.Rasterizer;

namespace Vysara.Tests;

/// <summary>
/// Fills of figures that cross themselves or each other, through the ported sweep-line tessellator: they fill exactly
/// what their fill rule says (checked against the winding number computed straight from the input, including random
/// self-crossing polygons), the antialiasing fringe runs only along the filled region's boundary, the result blends
/// once everywhere, and re-tessellating allocates nothing once warm.
/// </summary>
public sealed class SweepFillTests
{
	private const int Size = 100;
	private static readonly Rect Surface = new(0, 0, Size, Size);

	private static TessellatedPath Fill(string markup)
	{
		var output = new TessellatedPath();
		new Tessellator().Fill(Geometry.Parse(markup).CompositionPath, TessellationScale.Identity, output);
		return output;
	}

	private static bool IsInterior(VectorVertex v) => v.Distances == VectorVertex.NoEdges;

	private static bool InteriorCovers(TessellatedPath path, Vector2 p)
	{
		var v = path.Vertices;
		for (var i = 0; i < v.Length; i += 3)
		{
			if (IsInterior(v[i]) && Inside(v[i].Position, v[i + 1].Position, v[i + 2].Position, p))
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

	// The winding number of the closed polygons round p, from the input alone.
	private static int Winding(IReadOnlyList<Vector2[]> polygons, Vector2 p)
	{
		var winding = 0;
		foreach (var poly in polygons)
		{
			for (var i = 0; i < poly.Length; i++)
			{
				var a = poly[i];
				var b = poly[(i + 1) % poly.Length];
				var cross = (b.X - a.X) * (p.Y - a.Y) - (p.X - a.X) * (b.Y - a.Y);
				if (a.Y <= p.Y && b.Y > p.Y && cross > 0)
					winding++;
				else if (a.Y > p.Y && b.Y <= p.Y && cross < 0)
					winding--;
			}
		}
		return winding;
	}

	private static float DistanceToEdges(IReadOnlyList<Vector2[]> polygons, Vector2 p)
	{
		var best = float.MaxValue;
		foreach (var poly in polygons)
		{
			for (var i = 0; i < poly.Length; i++)
			{
				var a = poly[i];
				var ab = poly[(i + 1) % poly.Length] - a;
				var t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-12f), 0, 1);
				best = MathF.Min(best, Vector2.Distance(p, a + t * ab));
			}
		}
		return best;
	}

	private static string Markup(IReadOnlyList<Vector2[]> polygons, FillRule rule)
	{
		var c = CultureInfo.InvariantCulture;
		var parts = polygons.Select(poly => "M " + string.Join(" L ", poly.Select(p => $"{p.X.ToString("R", c)},{p.Y.ToString("R", c)}")) + " Z");
		return (rule == FillRule.Nonzero ? "F1 " : "") + string.Join(" ", parts);
	}

	// Every grid point over a pixel from any input edge is filled exactly when the rule says the winding is inside.
	private static void CheckAgainstWinding(IReadOnlyList<Vector2[]> polygons, FillRule rule, float step = 1f)
	{
		var path = Fill(Markup(polygons, rule));
		var all = polygons.SelectMany(p => p).ToArray();
		float minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
		var checkedPoints = 0;
		for (var y = minY - 2; y <= maxY + 2; y += step)
		{
			for (var x = minX - 2; x <= maxX + 2; x += step)
			{
				var p = new Vector2(x + 0.137f, y + 0.291f);
				if (DistanceToEdges(polygons, p) < 1.1f)
					continue;
				var w = Winding(polygons, p);
				var inside = rule == FillRule.Nonzero ? w != 0 : (w & 1) != 0;
				InteriorCovers(path, p).ShouldBe(inside, $"{Markup(polygons, rule)} at ({p.X}, {p.Y}), winding {w}");
				checkedPoints++;
			}
		}
		checkedPoints.ShouldBeGreaterThan(0);
	}

	private static Vector2[] P(params float[] xy) => [.. Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2(xy[2 * i], xy[2 * i + 1]))];

	[Test]
	[Arguments(FillRule.EvenOdd)]
	[Arguments(FillRule.Nonzero)]
	public void A_bow_tie_fills_both_its_triangles(FillRule rule) =>
		CheckAgainstWinding([P(0, 0, 40, 40, 40, 0, 0, 40)], rule);

	[Test]
	[Arguments(FillRule.EvenOdd)]
	[Arguments(FillRule.Nonzero)]
	public void A_pentagram_fills_its_centre_only_under_nonzero(FillRule rule)
	{
		var star = P(50, 5, 79, 95, 2, 40, 98, 40, 21, 95);
		CheckAgainstWinding([star], rule);
		InteriorCovers(Fill(Markup([star], rule)), new Vector2(50, 50)).ShouldBe(rule == FillRule.Nonzero);
	}

	[Test]
	[Arguments(FillRule.EvenOdd)]
	[Arguments(FillRule.Nonzero)]
	public void Overlapping_and_touching_figures_fill_by_the_rule(FillRule rule)
	{
		CheckAgainstWinding([P(0, 0, 40, 0, 40, 40, 0, 40), P(20, 20, 60, 20, 60, 60, 20, 60)], rule);
		CheckAgainstWinding([P(0, 0, 40, 0, 40, 40, 0, 40), P(20, 20, 20, 60, 60, 60, 60, 20)], rule); // opposite direction
		CheckAgainstWinding([P(0, 0, 20, 0, 20, 20, 0, 20), P(20, 0, 40, 0, 40, 20, 20, 20)], rule);   // sharing an edge
		CheckAgainstWinding([P(0, 0, 20, 0, 20, 20, 0, 20), P(20, 20, 40, 20, 40, 40, 20, 40)], rule); // sharing a corner
	}

	[Test]
	public void A_figure_folding_back_along_itself_fills_by_the_rule()
	{
		CheckAgainstWinding([P(0, 0, 20, 0, 20, 20, 20, 0, 40, 0, 40, 40, 0, 40)], FillRule.EvenOdd);
		CheckAgainstWinding([P(0, 0, 20, 0, 20, 20, 20, 0, 40, 0, 40, 40, 0, 40)], FillRule.Nonzero);
	}

	[Test]
	public void Random_self_crossing_polygons_fill_by_the_rule()
	{
		var random = new Random(12345);
		for (var n = 0; n < 60; n++)
		{
			var count = random.Next(3, 12);
			var polygons = new List<Vector2[]> { RandomPolygon(random, count) };
			if (n % 3 == 0)
				polygons.Add(RandomPolygon(random, random.Next(3, 8)));
			CheckAgainstWinding(polygons, n % 2 == 0 ? FillRule.EvenOdd : FillRule.Nonzero, step: 2f);
		}

		static Vector2[] RandomPolygon(Random random, int count) =>
			[.. Enumerable.Range(0, count).Select(_ => new Vector2(random.Next(0, 80), random.Next(0, 80)))];
	}

	[Test]
	public void The_fringe_runs_only_along_the_filled_regions_boundary()
	{
		// Two overlapping squares under nonzero merge into one region: no edge inside it may be antialiased.
		var union = Fill("F1 M 10,10 L 60,10 60,60 10,60 Z M 40,40 L 90,40 90,90 40,90 Z");
		var px = Render(union, PremultipliedHdrColor.FromStraight(1, 1, 1, 1));
		for (var t = 42; t < 58; t++)
		{
			Alpha(px, 60, t).ShouldBe(1f); // the first square's right edge, inside the second
			Alpha(px, t, 40).ShouldBe(1f); // the second square's top edge, inside the first
		}
		Alpha(px, 61, 20).ShouldBe(0f);    // the real boundary is still crisp
		Alpha(px, 59, 20).ShouldBe(1f);

		// Under even-odd the overlap is a hole, so those edges are boundaries.
		var holed = Render(Fill("M 10,10 L 60,10 60,60 10,60 Z M 40,40 L 90,40 90,90 40,90 Z"), PremultipliedHdrColor.FromStraight(1, 1, 1, 1));
		Alpha(holed, 50, 50).ShouldBe(0f);
		Alpha(holed, 30, 30).ShouldBe(1f);
	}

	[Test]
	[Arguments("F1 M 10,10 L 60,10 60,60 10,60 Z M 40,40 L 90,40 90,90 40,90 Z")]
	[Arguments("M 50,5 L 79,95 L 2,40 L 98,40 L 21,95 Z")]
	[Arguments("F1 M 50,5 L 79,95 L 2,40 L 98,40 L 21,95 Z")]
	[Arguments("M 10,10 L 90,90 L 90,10 L 10,90 Z")]
	public void A_crossing_fill_blends_once_with_no_gaps(string markup)
	{
		// Translucent, with no group: any overlap would show darker and any gap clear, deep inside the fill.
		var path = Fill(markup);
		var px = Render(path, PremultipliedHdrColor.FromStraight(1, 1, 1, 0.5f));
		var inside = 0;
		for (var y = 0; y < Size; y++)
		{
			for (var x = 0; x < Size; x++)
			{
				var p = new Vector2(x + 0.5f, y + 0.5f);
				if (!InteriorCovers(path, p) || NearAnyFringe(path, p))
					continue;
				Alpha(px, x, y).ShouldBe(0.5f, 1f / 255f, $"pixel ({x},{y})");
				inside++;
			}
		}
		inside.ShouldBeGreaterThan(200);
	}

	// Whether p is within two pixels of any fringe vertex, where coverage is legitimately partial.
	/// <summary>
	/// Whether p is within a pixel and a half of any fringe triangle, where coverage is legitimately
	/// partial.
	/// </summary>
	private static bool NearAnyFringe(TessellatedPath path, Vector2 p)
	{
		var vertices = path.Vertices;
		for (var i = 0; i < vertices.Length; i += 3)
		{
			if (IsInterior(vertices[i]))
				continue;
			Vector2 a = vertices[i].Position, b = vertices[i + 1].Position, c = vertices[i + 2].Position;
			if (Inside(a, b, c, p) || Segment(a, b) <= 1.5f || Segment(b, c) <= 1.5f || Segment(c, a) <= 1.5f)
				return true;
		}
		return false;

		float Segment(Vector2 a, Vector2 b)
		{
			var ab = b - a;
			var t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-12f), 0, 1);
			return Vector2.Distance(p, a + t * ab);
		}
	}

	[Test]
	public void Re_tessellating_a_crossing_fill_allocates_nothing_once_warm()
	{
		var path = Geometry.Parse("F1 M 50,5 L 79,95 L 2,40 L 98,40 L 21,95 Z M 10,10 L 60,10 60,60 10,60 Z").CompositionPath;
		var tessellator = new Tessellator();
		var output = new TessellatedPath();
		for (var i = 0; i < 4; i++)
			tessellator.Fill(path, TessellationScale.Identity, output);

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 20; i++)
				tessellator.Fill(path, TessellationScale.Identity, output);
		});
		output.TriangleCount.ShouldBeGreaterThan(0);
	}

	[Test]
	public void Degenerate_input_does_not_break_the_sweep()
	{
		// Repeated points, a spike, collinear overlapping edges, a zero-area figure and coincident figures.
		Fill("M 0,0 L 10,0 10,0 10,10 10,10 0,10 Z M 5,5 L 5,5 L 5,5 Z").TriangleCount.ShouldBeGreaterThan(0);
		Fill("M 0,0 L 20,0 L 10,0 L 30,0 L 30,10 L 0,10 Z").TriangleCount.ShouldBeGreaterThan(0);
		Fill("F1 M 0,0 L 10,0 10,10 0,10 Z M 0,0 L 10,0 10,10 0,10 Z").TriangleCount.ShouldBeGreaterThan(0); // winding 2
		Fill("M 0,0 L 10,0 10,10 0,10 Z M 0,0 L 10,0 10,10 0,10 Z").TriangleCount.ShouldBe(0);               // even: empty
		Fill("F1 M 0,0 L 10,0 10,10 0,10 Z M 0,0 L 0,10 10,10 10,0 Z").TriangleCount.ShouldBe(0);            // they cancel
	}

	private static byte[] Render(TessellatedPath path, PremultipliedHdrColor color)
	{
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.AddVector(path, Matrix3x2.Identity, color, Surface);
		var raster = new SoftwareRasterizer(Size, Size);
		raster.Clear(Vector4.Zero);
		raster.Render(builder.End());
		return raster.ResolveToRgba8();
	}

	private static float Alpha(byte[] px, int x, int y) => px[(y * Size + x) * 4 + 3] / 255f;
}
