using System.Globalization;
using System.Numerics;
using System.Text;

namespace BinaryVibrance.Vysara.Composition;

/// <summary>
/// A retained, versioned vector path: figures of lines and quadratic and cubic Béziers, with a fill rule. It is the
/// neutral form a geometry takes in the composition layer, named after WinUI's <c>CompositionPath</c>: what the public
/// tessellator reads, and what a foreign compositor renders natively. Elliptical arcs are stored as cubic Béziers of
/// at most 45° each, whose distance from the true ellipse is under 0.0005% of its radius.
/// <para>
/// It is mutable and retained like the rest of the composition content: rebuilding it with <see cref="Clear"/> and the
/// drawing calls reuses its storage, so a path that keeps its size stops allocating. Every change advances
/// <see cref="Version"/>, which work derived from the path (a tessellation) is keyed by.
/// </para>
/// </summary>
public sealed class CompositionPath
{
	private readonly RetainedBuffer<PathVerb> _verbs = new(16);
	private readonly RetainedBuffer<Vector2> _points = new(32);
	private readonly RetainedBuffer<FigureStart> _figures = new(4);
	private Vector2 _current;
	private bool _inFigure;
	private Rect _bounds;
	private uint _boundsVersion;

	private readonly record struct FigureStart(int Verb, int Point, bool Filled);

	/// <summary>Advances on every change; never 0, so a consumer can use 0 for "not seen yet".</summary>
	public uint Version { get; private set; } = 1;

	/// <summary>How overlapping figures and self-crossings decide what a fill covers. <see cref="CompositionFillRule.EvenOdd"/>
	/// by default, as WinUI's.</summary>
	public CompositionFillRule FillRule
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			Version++;
		}
	}

	/// <summary>The drawing commands, in order; each figure starts with <see cref="PathVerb.Move"/>.</summary>
	public ReadOnlySpan<PathVerb> Verbs => _verbs.Span;

	/// <summary>The points the verbs take, in order: one for a move or a line, two for a quadratic, three for a cubic.</summary>
	public ReadOnlySpan<Vector2> Points => _points.Span;

	/// <summary>The number of figures.</summary>
	public int FigureCount => _figures.Count;

	/// <summary>Whether the path draws nothing: no figures.</summary>
	public bool IsEmpty => _figures.Count == 0;

	/// <summary>Where figure <paramref name="index"/> lies in <see cref="Verbs"/> and <see cref="Points"/>.</summary>
	public PathFigureInfo GetFigure(int index)
	{
		var figures = _figures.Span;
		var start = figures[index];
		var endVerb = index + 1 < figures.Length ? figures[index + 1].Verb : _verbs.Count;
		var endPoint = index + 1 < figures.Length ? figures[index + 1].Point : _points.Count;
		var closed = endVerb > start.Verb && _verbs.Span[endVerb - 1] == PathVerb.Close;
		return new PathFigureInfo(start.Verb, endVerb - start.Verb, start.Point, endPoint - start.Point, closed, start.Filled);
	}

	/// <summary>
	/// The tight axis-aligned bounds of everything the path draws: its points, and the extremes of its curves rather
	/// than their control points. <see cref="Rect.Zero"/> for an empty path.
	/// </summary>
	public Rect Bounds
	{
		get
		{
			if (_boundsVersion != Version)
			{
				_bounds = ComputeBounds(Matrix3x2.Identity);
				_boundsVersion = Version;
			}
			return _bounds;
		}
	}

	/// <summary>
	/// The tight axis-aligned bounds of the path with every point mapped through <paramref name="transform"/>: the bounds
	/// of the mapped curves, which under a rotation are tighter than the mapped <see cref="Bounds"/>. Computed on each
	/// call, with no allocation.
	/// </summary>
	public Rect GetBounds(in Matrix3x2 transform) => transform.IsIdentity ? Bounds : ComputeBounds(transform);

	/// <summary>Remove every figure, keeping the storage, so the path can be rebuilt without allocating.</summary>
	public void Clear()
	{
		_verbs.Clear();
		_points.Clear();
		_figures.Clear();
		_inFigure = false;
		Version++;
	}

	/// <summary>Start a figure at <paramref name="point"/>. <paramref name="filled"/> false leaves its interior out of
	/// a fill, as WinUI's <c>PathFigure.IsFilled</c>.</summary>
	public void MoveTo(Vector2 point, bool filled = true)
	{
		_figures.Add(new FigureStart(_verbs.Count, _points.Count, filled));
		_verbs.Add(PathVerb.Move);
		_points.Add(point);
		_current = point;
		_inFigure = true;
		Version++;
	}

	public void LineTo(Vector2 point)
	{
		RequireFigure();
		_verbs.Add(PathVerb.Line);
		_points.Add(point);
		_current = point;
		Version++;
	}

	public void QuadraticTo(Vector2 control, Vector2 point)
	{
		RequireFigure();
		_verbs.Add(PathVerb.Quadratic);
		_points.Add(control);
		_points.Add(point);
		_current = point;
		Version++;
	}

	public void CubicTo(Vector2 control1, Vector2 control2, Vector2 point)
	{
		RequireFigure();
		_verbs.Add(PathVerb.Cubic);
		_points.Add(control1);
		_points.Add(control2);
		_points.Add(point);
		_current = point;
		Version++;
	}

	/// <summary>
	/// An elliptical arc from the current point to <paramref name="point"/>, in the endpoint form of WinUI's
	/// <c>ArcSegment</c>, stored as cubic Béziers. Radii too small to reach the end point grow, keeping their ratio, until
	/// they just do; a zero radius draws a straight line, and an arc that ends where it starts draws nothing, as SVG's
	/// arcs do.
	/// </summary>
	public void ArcTo(Vector2 radii, float rotationDegrees, bool isLargeArc, CompositionSweepDirection sweepDirection, Vector2 point)
	{
		RequireFigure();
		var start = _current;
		if (start == point)
			return;
		double rx = MathF.Abs(radii.X), ry = MathF.Abs(radii.Y);
		if (rx == 0 || ry == 0)
		{
			LineTo(point);
			return;
		}

		// The center parameterization of SVG's implementation notes (F.6.5), in double for the near-degenerate cases.
		var phi = rotationDegrees * Math.PI / 180;
		double cos = Math.Cos(phi), sin = Math.Sin(phi);
		double hx = (start.X - point.X) / 2.0, hy = (start.Y - point.Y) / 2.0;
		var x1 = cos * hx + sin * hy;
		var y1 = -sin * hx + cos * hy;
		var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
		if (lambda > 1)
		{
			var grow = Math.Sqrt(lambda);
			rx *= grow;
			ry *= grow;
		}
		double rx2 = rx * rx, ry2 = ry * ry;
		var num = rx2 * ry2 - rx2 * y1 * y1 - ry2 * x1 * x1;
		var den = rx2 * y1 * y1 + ry2 * x1 * x1;
		var coef = den > 0 ? Math.Sqrt(Math.Max(0, num / den)) : 0;
		var clockwise = sweepDirection == CompositionSweepDirection.Clockwise;
		if (isLargeArc == clockwise)
			coef = -coef;
		var cxp = coef * rx * y1 / ry;
		var cyp = -coef * ry * x1 / rx;
		var cx = cos * cxp - sin * cyp + (start.X + point.X) / 2.0;
		var cy = sin * cxp + cos * cyp + (start.Y + point.Y) / 2.0;
		var theta = Math.Atan2((y1 - cyp) / ry, (x1 - cxp) / rx);
		var sweep = Math.Atan2((-y1 - cyp) / ry, (-x1 - cxp) / rx) - theta;
		// With y down, a clockwise arc runs through increasing angles.
		if (clockwise && sweep < 0)
			sweep += 2 * Math.PI;
		else if (!clockwise && sweep > 0)
			sweep -= 2 * Math.PI;

		var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 4) - 1e-9));
		var step = sweep / pieces;
		var k = 4.0 / 3.0 * Math.Tan(step / 4);
		for (var i = 0; i < pieces; i++)
		{
			var a0 = theta + i * step;
			var a1 = a0 + step;
			double c0 = Math.Cos(a0), s0 = Math.Sin(a0), c1 = Math.Cos(a1), s1 = Math.Sin(a1);
			var p1 = Map(c0 - k * s0, s0 + k * c0);
			var p2 = Map(c1 + k * s1, s1 - k * c1);
			var p3 = i == pieces - 1 ? point : Map(c1, s1);
			CubicTo(p1, p2, p3);
		}

		// A point on the unit circle, scaled by the radii, rotated and moved to the center.
		Vector2 Map(double ux, double uy) =>
			new((float)(cx + rx * cos * ux - ry * sin * uy), (float)(cy + rx * sin * ux + ry * cos * uy));
	}

	/// <summary>Close the current figure with a line back to its start. The next figure starts with
	/// <see cref="MoveTo"/>.</summary>
	public void Close()
	{
		RequireFigure();
		_verbs.Add(PathVerb.Close);
		_current = _points.Span[_figures.Span[^1].Point];
		_inFigure = false;
		Version++;
	}

	/// <summary>Add a closed figure of <paramref name="rect"/> with its corners rounded by quarter-ellipse arcs of
	/// <paramref name="radii"/>, each at most half the side it rounds, clockwise from the top edge; square corners when
	/// either radius is 0, and nothing when the rectangle is empty.</summary>
	internal void AddRoundedRectangle(in Rect rect, Vector2 radii)
	{
		if (!(rect.Width > 0f) || !(rect.Height > 0f))
			return;
		var r = new Vector2(Math.Clamp(radii.X, 0f, rect.Width / 2f), Math.Clamp(radii.Y, 0f, rect.Height / 2f));
		if (r.X == 0f || r.Y == 0f)
			r = Vector2.Zero;
		float x0 = rect.X, y0 = rect.Y, x1 = rect.Right, y1 = rect.Bottom;
		MoveTo(new Vector2(x0 + r.X, y0));
		Side(new Vector2(x1 - r.X, y0), new Vector2(x1, y0 + r.Y));
		Side(new Vector2(x1, y1 - r.Y), new Vector2(x1 - r.X, y1));
		Side(new Vector2(x0 + r.X, y1), new Vector2(x0, y1 - r.Y));
		Side(new Vector2(x0, y0 + r.Y), new Vector2(x0 + r.X, y0));
		Close();

		// A side up to where the next corner starts, then the corner; a square corner adds nothing.
		void Side(Vector2 end, Vector2 cornerEnd)
		{
			LineTo(end);
			if (r != Vector2.Zero)
				ArcTo(r, 0f, false, CompositionSweepDirection.Clockwise, cornerEnd);
		}
	}

	/// <summary>Add a closed figure of <paramref name="rect"/> with each corner rounded by a circular arc of its own radius
	/// from <paramref name="radii"/>, fitted to the rectangle (<see cref="CompositionCornerRadius.FitTo"/>), clockwise from
	/// the top edge; nothing when the rectangle is empty.</summary>
	internal void AddRoundedRectangle(in Rect rect, CompositionCornerRadius radii)
	{
		if (!(rect.Width > 0f) || !(rect.Height > 0f))
			return;
		var r = radii.FitTo(new Vector2(rect.Width, rect.Height));
		float x0 = rect.X, y0 = rect.Y, x1 = rect.Right, y1 = rect.Bottom;
		MoveTo(new Vector2(x0 + r.TopLeft, y0));
		Side(new Vector2(x1 - r.TopRight, y0), new Vector2(x1, y0 + r.TopRight), r.TopRight);
		Side(new Vector2(x1, y1 - r.BottomRight), new Vector2(x1 - r.BottomRight, y1), r.BottomRight);
		Side(new Vector2(x0 + r.BottomLeft, y1), new Vector2(x0, y1 - r.BottomLeft), r.BottomLeft);
		Side(new Vector2(x0, y0 + r.TopLeft), new Vector2(x0 + r.TopLeft, y0), r.TopLeft);
		Close();

		// A side up to where the next corner starts, then the corner; a square corner adds nothing.
		void Side(Vector2 end, Vector2 cornerEnd, float radius)
		{
			LineTo(end);
			if (radius > 0f)
				ArcTo(new Vector2(radius), 0f, false, CompositionSweepDirection.Clockwise, cornerEnd);
		}
	}

	/// <summary>Add a closed figure of the ellipse inscribed in <paramref name="rect"/>, clockwise from its rightmost
	/// point, as WinUI's ellipse runs; nothing when the rectangle is empty.</summary>
	internal void AddEllipse(in Rect rect)
	{
		if (!(rect.Width > 0f) || !(rect.Height > 0f))
			return;
		var radii = new Vector2(rect.Width / 2f, rect.Height / 2f);
		var center = new Vector2(rect.X, rect.Y) + radii;
		MoveTo(center + new Vector2(radii.X, 0f));
		ArcTo(radii, 0f, false, CompositionSweepDirection.Clockwise, center - new Vector2(radii.X, 0f));
		ArcTo(radii, 0f, false, CompositionSweepDirection.Clockwise, center + new Vector2(radii.X, 0f));
		Close();
	}

	/// <summary>Add every figure of <paramref name="other"/> to this path, as they are, keeping this path's fill rule.
	/// A figure this path has open stays open; the next drawing call after this one continues the last figure
	/// appended.</summary>
	public void Append(CompositionPath other) => Append(other, Matrix3x2.Identity);

	/// <summary>Add every figure of <paramref name="other"/> to this path with its points mapped through
	/// <paramref name="transform"/>, which maps its curves exactly; otherwise as <see cref="Append(CompositionPath)"/>.</summary>
	public void Append(CompositionPath other, in Matrix3x2 transform)
	{
		var identity = transform.IsIdentity;
		var m = transform;
		var points = other.Points;
		for (var f = 0; f < other.FigureCount; f++)
		{
			var figure = other.GetFigure(f);
			var p = figure.FirstPoint;
			var verbs = other.Verbs.Slice(figure.FirstVerb, figure.VerbCount);
			foreach (var verb in verbs)
			{
				switch (verb)
				{
					case PathVerb.Move: MoveTo(Map(points[p++]), figure.IsFilled); break;
					case PathVerb.Line: LineTo(Map(points[p++])); break;
					case PathVerb.Quadratic: QuadraticTo(Map(points[p]), Map(points[p + 1])); p += 2; break;
					case PathVerb.Cubic: CubicTo(Map(points[p]), Map(points[p + 1]), Map(points[p + 2])); p += 3; break;
					default: Close(); break;
				}
			}
		}

		Vector2 Map(Vector2 v) => identity ? v : Vector2.Transform(v, m);
	}

	/// <summary>The path as WinUI path markup (<c>F1</c> when nonzero, then <c>M</c>, <c>L</c>, <c>Q</c>, <c>C</c> and
	/// <c>Z</c> commands), with round-trippable numbers: the form <c>Dump()</c> shows.</summary>
	public override string ToString()
	{
		var sb = new StringBuilder();
		if (FillRule == CompositionFillRule.Nonzero)
			sb.Append("F1 ");
		var points = Points;
		var p = 0;
		foreach (var verb in Verbs)
		{
			switch (verb)
			{
				case PathVerb.Move: sb.Append('M'); AppendPoints(sb, points, ref p, 1); break;
				case PathVerb.Line: sb.Append('L'); AppendPoints(sb, points, ref p, 1); break;
				case PathVerb.Quadratic: sb.Append('Q'); AppendPoints(sb, points, ref p, 2); break;
				case PathVerb.Cubic: sb.Append('C'); AppendPoints(sb, points, ref p, 3); break;
				default: sb.Append('Z'); break;
			}
			sb.Append(' ');
		}
		return sb.ToString().TrimEnd();
	}

	private static void AppendPoints(StringBuilder sb, ReadOnlySpan<Vector2> points, ref int index, int count)
	{
		var c = CultureInfo.InvariantCulture;
		for (var i = 0; i < count; i++, index++)
			sb.Append(c, $" {points[index].X:R},{points[index].Y:R}");
	}

	private void RequireFigure()
	{
		if (!_inFigure)
			throw new InvalidOperationException("A path draws inside a figure: start one with MoveTo first, including after Close.");
	}

	private Rect ComputeBounds(Matrix3x2 transform)
	{
		var points = Points;
		if (points.Length == 0)
			return Rect.Zero;
		// An affine map takes a Bézier's control points to the control points of the mapped curve, so the extremes are
		// found the same way on the mapped points.
		var identity = transform.IsIdentity;
		var min = new Vector2(float.MaxValue);
		var max = new Vector2(float.MinValue);
		var p = 0;
		var last = Vector2.Zero;
		foreach (var verb in Verbs)
		{
			switch (verb)
			{
				case PathVerb.Move or PathVerb.Line:
					last = Map(points[p++]);
					Grow(last);
					break;
				case PathVerb.Quadratic:
					{
						var (c, e) = (Map(points[p]), Map(points[p + 1]));
						p += 2;
						Grow(e);
						// The quadratic's derivative is zero where t = (p0 - c) / (p0 - 2c + e), per axis.
						GrowAt(QuadraticExtreme(last.X, c.X, e.X), last, c, e);
						GrowAt(QuadraticExtreme(last.Y, c.Y, e.Y), last, c, e);
						last = e;
						break;
					}
				case PathVerb.Cubic:
					{
						var (c1, c2, e) = (Map(points[p]), Map(points[p + 1]), Map(points[p + 2]));
						p += 3;
						Grow(e);
						GrowCubicExtremes(last.X, c1.X, c2.X, e.X, last, c1, c2, e);
						GrowCubicExtremes(last.Y, c1.Y, c2.Y, e.Y, last, c1, c2, e);
						last = e;
						break;
					}
			}
		}
		return new Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y);

		Vector2 Map(Vector2 v) => identity ? v : Vector2.Transform(v, transform);

		void Grow(Vector2 v)
		{
			min = Vector2.Min(min, v);
			max = Vector2.Max(max, v);
		}

		void GrowAt(float t, Vector2 p0, Vector2 c, Vector2 e)
		{
			if (t is > 0 and < 1)
			{
				var u = 1 - t;
				Grow(u * u * p0 + 2 * u * t * c + t * t * e);
			}
		}

		void GrowCubicExtremes(float a, float b, float c, float d, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
		{
			// The derivative 3[(b-a)(1-t)² + 2(c-b)t(1-t) + (d-c)t²] is a quadratic in t: q2 t² + q1 t + q0.
			var q2 = -a + 3 * b - 3 * c + d;
			var q1 = 2 * (a - 2 * b + c);
			var q0 = b - a;
			if (MathF.Abs(q2) < 1e-12f)
			{
				if (MathF.Abs(q1) > 1e-12f)
					GrowCubic(-q0 / q1);
				return;
			}
			var disc = q1 * q1 - 4 * q2 * q0;
			if (disc < 0)
				return;
			var root = MathF.Sqrt(disc);
			GrowCubic((-q1 + root) / (2 * q2));
			GrowCubic((-q1 - root) / (2 * q2));

			void GrowCubic(float t)
			{
				if (t is > 0 and < 1)
				{
					var u = 1 - t;
					Grow(u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3);
				}
			}
		}

		static float QuadraticExtreme(float a, float b, float c)
		{
			var den = a - 2 * b + c;
			return MathF.Abs(den) < 1e-12f ? -1 : (a - b) / den;
		}
	}
}
