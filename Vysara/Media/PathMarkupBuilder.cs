using System.Runtime.InteropServices;
using Vysara.Markup;

namespace Vysara.Media;

/// <summary>
/// Builds a <see cref="PathGeometry"/> from parsed path markup: one figure per move, and one segment per run of the
/// same command, so <c>L 1,1 2,2 3,3</c> becomes a single <see cref="PolyLineSegment"/> and one line stays a
/// <see cref="LineSegment"/>. Arcs are one <see cref="ArcSegment"/> each, as they cannot share one.
/// </summary>
internal sealed class PathMarkupBuilder(PathGeometry geometry) : IPathMarkupSink
{
	private enum Run { None, Line, Quadratic, Cubic }

	private readonly List<Point> _run = [];
	private Run _kind;
	private PathFigure? _figure;

	public void SetFillRule(bool nonzero) => geometry.FillRule = nonzero ? FillRule.Nonzero : FillRule.EvenOdd;

	public void BeginFigure(double x, double y)
	{
		Flush();
		_figure = new PathFigure { StartPoint = P(x, y) };
		geometry.Figures.Add(_figure);
	}

	public void LineTo(double x, double y) => Extend(Run.Line, P(x, y));

	public void QuadraticTo(double x1, double y1, double x, double y)
	{
		Extend(Run.Quadratic, P(x1, y1));
		_run.Add(P(x, y));
	}

	public void CubicTo(double x1, double y1, double x2, double y2, double x, double y)
	{
		Extend(Run.Cubic, P(x1, y1));
		_run.Add(P(x2, y2));
		_run.Add(P(x, y));
	}

	public void ArcTo(double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool clockwise, double x, double y)
	{
		Flush();
		_figure!.Segments.Add(new ArcSegment
		{
			Point = P(x, y),
			Size = new Size((float)radiusX, (float)radiusY),
			RotationAngle = (float)rotationDegrees,
			IsLargeArc = largeArc,
			SweepDirection = clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
		});
	}

	public void CloseFigure()
	{
		Flush();
		_figure!.IsClosed = true;
	}

	/// <summary>Add the run still being collected; call once the markup has been read.</summary>
	public void Finish() => Flush();

	private void Extend(Run kind, Point first)
	{
		if (_kind != kind)
		{
			Flush();
			_kind = kind;
		}
		_run.Add(first);
	}

	private void Flush()
	{
		if (_kind == Run.None)
			return;
		var points = _run;
		PathSegment segment = _kind switch
		{
			Run.Line when points.Count == 1 => new LineSegment { Point = points[0] },
			Run.Quadratic when points.Count == 2 => new QuadraticBezierSegment { Point1 = points[0], Point2 = points[1] },
			Run.Cubic when points.Count == 3 => new BezierSegment { Point1 = points[0], Point2 = points[1], Point3 = points[2] },
			Run.Line => new PolyLineSegment { Points = Collect(points) },
			Run.Quadratic => new PolyQuadraticBezierSegment { Points = Collect(points) },
			_ => new PolyBezierSegment { Points = Collect(points) },
		};
		_figure!.Segments.Add(segment);
		_run.Clear();
		_kind = Run.None;
	}

	private static PointCollection Collect(List<Point> points)
	{
		var collection = new PointCollection();
		collection.SetAll(CollectionsMarshal.AsSpan(points));
		return collection;
	}

	private static Point P(double x, double y) => new((float)x, (float)y);
}
