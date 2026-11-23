using Vysara.Text.Geometry;

namespace Vysara.Text.OpenType;

/// <summary>
/// Converts a TrueType <c>glyf</c> contour's on/off-curve points into edge segments. Off-curve points are
/// quadratic control points; two consecutive off-curve points imply an on-curve point at their midpoint; a contour
/// with no on-curve point is anchored at the midpoint of its first and last points.
/// </summary>
internal static class ContourBuilder
{
	public static Contour Build(IReadOnlyList<GlyphContourPoint> points)
	{
		var contour = new Contour();
		var n = points.Count;
		if (n == 0)
			return contour;

		// Step 1: produce a cyclic sequence that starts on an on-curve point.
		var firstOn = -1;
		for (var i = 0; i < n; i++)
		{
			if (points[i].OnCurve)
			{
				firstOn = i;
				break;
			}
		}

		var expanded = new List<GlyphContourPoint>(n + 4);
		if (firstOn >= 0)
		{
			for (var k = 0; k < n; k++)
				expanded.Add(points[(firstOn + k) % n]);
		}
		else
		{
			// No on-curve point at all: anchor at the midpoint of the last and first off-curve points.
			expanded.Add(new GlyphContourPoint(Vector2D.Mix(points[n - 1].Position, points[0].Position, 0.5), true));
			for (var k = 0; k < n; k++)
				expanded.Add(points[k]);
		}

		// Step 2: insert implied on-curve midpoints between consecutive off-curve points (cyclically).
		var seq = new List<GlyphContourPoint>(expanded.Count * 2);
		for (var i = 0; i < expanded.Count; i++)
		{
			var cur = expanded[i];
			seq.Add(cur);
			var next = expanded[(i + 1) % expanded.Count];
			if (!cur.OnCurve && !next.OnCurve)
				seq.Add(new GlyphContourPoint(Vector2D.Mix(cur.Position, next.Position, 0.5), true));
		}

		// Step 3: emit line / quadratic edges, closing back to the start.
		var start = seq[0].Position;
		var pen = start;
		var idx = 1;
		while (idx < seq.Count)
		{
			var node = seq[idx];
			if (node.OnCurve)
			{
				AddLine(contour, pen, node.Position);
				pen = node.Position;
				idx++;
			}
			else
			{
				var nextOn = idx + 1 < seq.Count ? seq[idx + 1].Position : start;
				contour.Add(new QuadraticSegment(pen, node.Position, nextOn));
				pen = nextOn;
				idx += 2;
			}
		}

		AddLine(contour, pen, start);
		return contour;
	}

	private static void AddLine(Contour contour, Vector2D a, Vector2D b)
	{
		// Skip zero-length closing/segment artefacts (a control already landed on the start).
		if (a.X == b.X && a.Y == b.Y)
			return;
		contour.Add(new LinearSegment(a, b));
	}
}
