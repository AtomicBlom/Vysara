using System.Globalization;
using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Drawing;

public sealed partial class Tessellator
{
	/// <summary>One contour to fill: a range of <see cref="_contour"/>, its signed area, and what the nesting analysis
	/// decided about it.</summary>
	private struct Contour
	{
		public int Start, Count;
		public float Area;
		public int Parent;      // the smallest contour containing it, or -1
		public bool Filled;     // whether the region just inside it is filled
		public bool Boundary;   // whether it separates filled from unfilled
		public int Owner;       // for a hole, the outer boundary whose region it is cut from
	}

	/// <summary>The farthest a fringe vertex moves along its miter, in half pixels; sharper corners are cut short.</summary>
	private const float MaxMiter = 4f;

	private readonly RetainedBuffer<Vector2> _contour = new(64);
	private readonly RetainedBuffer<Contour> _contours = new(4);
	private readonly RetainedBuffer<Vector2> _miters = new(64);
	private readonly RetainedBuffer<int> _fillTriangles = new(192);
	private readonly RetainedBuffer<(int Start, int Count)> _holeRings = new(4);
	private readonly RetainedBuffer<(float MinX, float MaxX, int Contour, int Edge)> _edges = new(64);
	private readonly EarClipper _earClipper = new();

	/// <summary>
	/// Tessellate the fill of <paramref name="path"/> into <paramref name="output"/> under its fill rule: every filled
	/// figure, closed or not (an open one is closed by a straight line for filling), with holes where figures nest. The
	/// interior is inset by half a pixel and each true edge gets a one-pixel fringe straddling it, carrying its distance,
	/// so the edge's coverage ramp sits exactly on it.
	/// <para>
	/// Near-exact rather than exact: at a reflex corner each fringe takes its own edge's ramp, so the notch's
	/// antialiasing is a little light where the two ramps would combine; features under a pixel wide can fold the inset
	/// interior over, and corners sharper than about 30° have their fringe cut short.
	/// </para>
	/// </summary>
	/// <exception cref="NotSupportedException">A figure crosses itself or another figure, or two touch.</exception>
	public void Fill(CompositionPath path, TessellationScale scale, TessellatedPath output)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(output);
		output.Begin(isStroke: false, path, null, scale);
		var margin = scale.HalfPixel;
		Flatten(path, scale.Tolerance);
		CollectContours();
		if (_contours.Count == 0)
		{
			output.Finish(0, 0);
			return;
		}
		RequireSimple();
		ResolveNesting(path.FillRule);
		ComputeMiters();

		// The interior: each outer boundary with its holes, triangulated, its corners moved half a pixel in.
		var points = _contour.Span;
		for (var c = 0; c < _contours.Count; c++)
		{
			var outer = _contours[c];
			if (!outer.Boundary || !outer.Filled)
				continue;
			_holeRings.Clear();
			for (var h = 0; h < _contours.Count; h++)
			{
				var hole = _contours[h];
				if (hole.Boundary && !hole.Filled && hole.Owner == c)
					_holeRings.Add((hole.Start, hole.Count));
			}
			_fillTriangles.Clear();
			_earClipper.Triangulate(points, (outer.Start, outer.Count), _holeRings.Span, _fillTriangles);
			var interior = VectorVertex.NoEdges;
			foreach (var i in _fillTriangles.Span)
				output.Triangles.Add(new VectorVertex(points[i] + margin * _miters[i], interior, 0));
		}

		// The fringe: a quad along every boundary edge, from half a pixel inside to half a pixel outside, carrying the
		// edge's distance. Neighbouring quads share their ends, the miter lines, so the fill is watertight.
		for (var c = 0; c < _contours.Count; c++)
		{
			var contour = _contours[c];
			if (!contour.Boundary)
				continue;
			for (var k = 0; k < contour.Count; k++)
			{
				var i = contour.Start + k;
				var j = contour.Start + (k + 1) % contour.Count;
				var a = points[i];
				var b = points[j];
				var normal = Vector2.Normalize(StrokePrimitives.Normal(b - a));
				var inA = new VectorVertex(a + margin * _miters[i], Edge(a + margin * _miters[i]), 0);
				var inB = new VectorVertex(b + margin * _miters[j], Edge(b + margin * _miters[j]), 0);
				var outA = new VectorVertex(a - margin * _miters[i], Edge(a - margin * _miters[i]), 0);
				var outB = new VectorVertex(b - margin * _miters[j], Edge(b - margin * _miters[j]), 0);
				StrokePrimitives.Triangle(output.Triangles, inA, inB, outB);
				StrokePrimitives.Triangle(output.Triangles, inA, outB, outA);

				Vector4 Edge(Vector2 p) => new(Vector2.Dot(p - a, normal), VectorVertex.Unused, VectorVertex.Unused, VectorVertex.Unused);
			}
		}
		output.Finish(0, 0);
	}

	// Every filled figure with some area becomes a contour, implicitly closed.
	private void CollectContours()
	{
		_contour.Clear();
		_contours.Clear();
		var points = _points.Span;
		foreach (var figure in _figures.Span)
		{
			if (!figure.Filled || figure.Count < 3)
				continue;
			var start = _contour.Count;
			var area = 0f;
			for (var k = 0; k < figure.Count; k++)
			{
				var a = points[figure.First + k];
				var b = points[figure.First + (k + 1) % figure.Count];
				_contour.Add(a);
				area += a.X * b.Y - b.X * a.Y;
			}
			area /= 2f;
			// A figure whose points all lie on one line encloses nothing. One with no net area that is not a line, such as a
			// bow tie, still goes on, to be refused for crossing itself.
			if (MathF.Abs(area) <= 1e-9f && Collinear(_contour.Span[start..]))
			{
				_contour.Truncate(start);
				continue;
			}
			_contours.Add(new Contour { Start = start, Count = figure.Count, Area = area, Parent = -1, Owner = -1 });
		}

		static bool Collinear(ReadOnlySpan<Vector2> ring)
		{
			var far = ring[0];
			foreach (var p in ring)
			{
				if (Vector2.DistanceSquared(p, ring[0]) > Vector2.DistanceSquared(far, ring[0]))
					far = p;
			}
			var axis = far - ring[0];
			var length = axis.Length();
			if (length == 0)
				return true;
			foreach (var p in ring)
			{
				var d = p - ring[0];
				if (MathF.Abs(d.X * axis.Y - d.Y * axis.X) / length > 1e-6f * MathF.Max(1f, length))
					return false;
			}
			return true;
		}
	}

	// Throw if any two edges of any contours cross or touch, other than neighbours meeting at their shared point.
	private void RequireSimple()
	{
		_edges.Clear();
		var points = _contour.Span;
		for (var c = 0; c < _contours.Count; c++)
		{
			var contour = _contours[c];
			for (var k = 0; k < contour.Count; k++)
			{
				var a = points[contour.Start + k];
				var b = points[contour.Start + (k + 1) % contour.Count];
				_edges.Add((MathF.Min(a.X, b.X), MathF.Max(a.X, b.X), c, k));
			}
		}

		// Sweep the edges in order of their left end, testing each against those whose x ranges overlap it.
		_edges.MutableSpan.Sort(static (a, b) => a.MinX.CompareTo(b.MinX));
		var edges = _edges.Span;
		for (var i = 0; i < edges.Length; i++)
		{
			for (var j = i + 1; j < edges.Length && edges[j].MinX <= edges[i].MaxX; j++)
			{
				if (Crosses(edges[i].Contour, edges[i].Edge, edges[j].Contour, edges[j].Edge, out var at))
					throw new NotSupportedException(
						$"Filling a figure that crosses itself or another figure is not supported: the outline crosses near ({at.X.ToString("0.###", CultureInfo.InvariantCulture)}, {at.Y.ToString("0.###", CultureInfo.InvariantCulture)}).");
			}
		}
	}

	private bool Crosses(int c1, int k1, int c2, int k2, out Vector2 at)
	{
		var points = _contour.Span;
		var first = _contours[c1];
		var second = _contours[c2];
		var p1 = points[first.Start + k1];
		var p2 = points[first.Start + (k1 + 1) % first.Count];
		var q1 = points[second.Start + k2];
		var q2 = points[second.Start + (k2 + 1) % second.Count];
		at = p1;

		if (c1 == c2)
		{
			var n = first.Count;
			var next = (k1 + 1) % n == k2;
			var previous = (k2 + 1) % n == k1;
			if (next || previous)
			{
				// Neighbours share a point; they only overlap if one folds back along the other.
				if (n == 3 && next && previous)
					return false;
				var (u, v) = next ? (p2 - p1, q2 - q1) : (q2 - q1, p2 - p1);
				at = next ? p2 : q2;
				return MathF.Abs(u.X * v.Y - u.Y * v.X) <= 1e-9f * u.Length() * v.Length() && Vector2.Dot(u, v) < 0;
			}
		}

		var o1 = Orient(p1, p2, q1);
		var o2 = Orient(p1, p2, q2);
		var o3 = Orient(q1, q2, p1);
		var o4 = Orient(q1, q2, p2);
		if (o1 * o2 < 0 && o3 * o4 < 0)
		{
			var t = o1 / (o1 - o2);
			at = q1 + (float)t * (q2 - q1);
			return true;
		}
		// Touching or collinear overlap.
		if ((o1 == 0 && Within(p1, p2, q1, out at)) || (o2 == 0 && Within(p1, p2, q2, out at))
			|| (o3 == 0 && Within(q1, q2, p1, out at)) || (o4 == 0 && Within(q1, q2, p2, out at)))
			return true;
		return false;

		static double Orient(Vector2 a, Vector2 b, Vector2 c) => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

		static bool Within(Vector2 a, Vector2 b, Vector2 p, out Vector2 at)
		{
			at = p;
			return p.X >= MathF.Min(a.X, b.X) && p.X <= MathF.Max(a.X, b.X) && p.Y >= MathF.Min(a.Y, b.Y) && p.Y <= MathF.Max(a.Y, b.Y);
		}
	}

	// Find each contour's parent (the smallest contour around it), wind the regions under the fill rule, and decide which
	// contours are boundaries: outers around a filled region (made to have a positive area) and holes in one (negative).
	private void ResolveNesting(FillRule rule)
	{
		var points = _contour.Span;
		for (var c = 0; c < _contours.Count; c++)
		{
			var probe = points[_contours[c].Start];
			var best = -1;
			for (var o = 0; o < _contours.Count; o++)
			{
				if (o == c || MathF.Abs(_contours[o].Area) <= MathF.Abs(_contours[c].Area))
					continue;
				if ((best < 0 || MathF.Abs(_contours[o].Area) < MathF.Abs(_contours[best].Area)) && Contains(_contours[o], probe))
					best = o;
			}
			_contours[c].Parent = best;
		}

		// The region just inside a contour is filled by its winding number: its own direction plus every contour around it.
		for (var c = 0; c < _contours.Count; c++)
		{
			var winding = 0;
			for (var p = c; p >= 0; p = _contours[p].Parent)
				winding += _contours[p].Area > 0 ? 1 : -1;
			_contours[c].Filled = rule == FillRule.Nonzero ? winding != 0 : (winding & 1) != 0;
		}

		for (var c = 0; c < _contours.Count; c++)
		{
			ref var contour = ref _contours[c];
			var parentFilled = contour.Parent >= 0 && _contours[contour.Parent].Filled;
			contour.Boundary = contour.Filled != parentFilled;
		}

		for (var c = 0; c < _contours.Count; c++)
		{
			ref var contour = ref _contours[c];
			if (!contour.Boundary)
				continue;
			// A hole belongs to the nearest boundary around it, which is an outer.
			var owner = c;
			if (!contour.Filled)
			{
				owner = contour.Parent;
				while (owner >= 0 && !_contours[owner].Boundary)
					owner = _contours[owner].Parent;
			}
			contour.Owner = owner;
			if (contour.Filled != contour.Area > 0)
				Reverse(ref contour);
		}
	}

	private bool Contains(in Contour contour, Vector2 point)
	{
		var points = _contour.Span.Slice(contour.Start, contour.Count);
		var inside = false;
		for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
		{
			if ((points[i].Y > point.Y) != (points[j].Y > point.Y)
				&& point.X < (points[j].X - points[i].X) * (point.Y - points[i].Y) / (points[j].Y - points[i].Y) + points[i].X)
				inside = !inside;
		}
		return inside;
	}

	private void Reverse(ref Contour contour)
	{
		for (int i = contour.Start, j = contour.Start + contour.Count - 1; i < j; i++, j--)
			(_contour[i], _contour[j]) = (_contour[j], _contour[i]);
		contour.Area = -contour.Area;
	}

	// For each boundary point, the direction it moves to inset the fill by one unit: along the miter of its two edges'
	// inward normals, scaled so both edges move by one, cut short at MaxMiter.
	private void ComputeMiters()
	{
		_miters.Clear();
		var points = _contour.Span;
		for (var i = 0; i < points.Length; i++)
			_miters.Add(Vector2.Zero);
		for (var c = 0; c < _contours.Count; c++)
		{
			var contour = _contours[c];
			if (!contour.Boundary)
				continue;
			for (var k = 0; k < contour.Count; k++)
			{
				var prev = points[contour.Start + (k + contour.Count - 1) % contour.Count];
				var at = points[contour.Start + k];
				var next = points[contour.Start + (k + 1) % contour.Count];
				var n1 = Vector2.Normalize(StrokePrimitives.Normal(at - prev));
				var n2 = Vector2.Normalize(StrokePrimitives.Normal(next - at));
				var sum = n1 + n2;
				var miter = sum.LengthSquared() < 1e-12f ? n2 : sum / (1f + Vector2.Dot(n1, n2));
				var length = miter.Length();
				if (length > MaxMiter)
					miter *= MaxMiter / length;
				_miters[contour.Start + k] = miter;
			}
		}
	}
}
