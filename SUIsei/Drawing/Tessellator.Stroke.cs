using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Drawing;

public sealed partial class Tessellator
{
	/// <summary>The most dashes one tessellation draws; a finer pattern throws rather than drawing a near-solid line
	/// from millions of triangles.</summary>
	public const int MaxDashes = 100_000;

	// Dash intervals of the figure being stroked, in figure-local arc length.
	private readonly RetainedBuffer<(float A, float B)> _dashes = new(16);
	private readonly RetainedBuffer<float> _pattern = new(8);

	/// <summary>
	/// Tessellate the stroke of <paramref name="path"/> into <paramref name="output"/>: WinUI's line joins (miter, cut off
	/// at the miter limit; bevel; round), its caps at the start and end of each open figure (flat, square, round,
	/// triangle) and its dashes, in multiples of the thickness, restarting at each figure and capped with the dash cap.
	/// A figure with no length that still draws (<c>M 5,5 L 5,5</c>) is a dot, its caps back to back. Every vertex
	/// carries its arc length along the whole geometry, which <see cref="TessellatedPath.Emit{TSink}(ref TSink, PathTrim)"/> trims by.
	/// </summary>
	/// <exception cref="InvalidOperationException">The dash pattern would draw more than <see cref="MaxDashes"/> dashes.</exception>
	public void Stroke(CompositionPath path, CompositionStrokeStyle style, TessellationScale scale, TessellatedPath output)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(style);
		ArgumentNullException.ThrowIfNull(output);
		output.Begin(isStroke: true, path, style, scale);
		var h = style.Thickness / 2f;
		var margin = scale.HalfPixel;
		var tolerance = scale.Tolerance;
		output.HalfThickness = h;
		output.Margin = margin;
		output.Tolerance = tolerance;
		output.TrimStartCap = style.StartCap;
		output.TrimEndCap = style.EndCap;
		if (!(h > 0) || !float.IsFinite(h))
		{
			output.Finish(0, 0);
			return;
		}

		Flatten(path, tolerance);
		_segmentCorners.Clear();
		var dashed = PreparePattern(style);
		var offset = 0f;
		var bodies = 0;
		var dashCount = 0;
		foreach (var figure in _figures.Span)
		{
			if (!figure.Draws)
				continue;
			var firstSegment = output.Segments.Count;
			var length = AddSegments(figure, output);
			var segments = output.Segments.Count - firstSegment;
			var runs = new FigureRuns(firstSegment, segments, length, offset, figure.Closed && length > 0);

			if (!dashed)
			{
				if (runs.Closed)
					bodies += AddRun(output, style, runs, 0, length, loop: true, default, default);
				else
					bodies += AddRun(output, style, runs, 0, length, loop: false, style.StartCap, style.EndCap);
			}
			else
			{
				DashIntervals(length, style.DashOffset * style.Thickness, runs.Closed, ref dashCount);
				var dashes = _dashes.Span;
				for (var i = 0; i < dashes.Length; i++)
				{
					var (a, b) = dashes[i];
					// On an open figure a dash at its start or end takes that end's cap; elsewhere the dash cap.
					var startCap = !runs.Closed && a <= 0 ? style.StartCap : style.DashCap;
					var endCap = !runs.Closed && b >= length ? style.EndCap : style.DashCap;
					bodies += AddRun(output, style, runs, a, b, loop: runs.Closed && a <= 0 && b >= length, startCap, endCap);
				}
			}
			offset += length;
		}
		output.Finish(offset, bodies);
	}

	/// <summary>Where a figure's segments are in the output, and its length and offset along the whole geometry.</summary>
	private readonly record struct FigureRuns(int FirstSegment, int SegmentCount, float Length, float Offset, bool Closed);

	// Add the figure's straight segments (and a closed figure's closing one) with their arc lengths; returns its length.
	// A figure with no length gets one zero-length segment, along x, for its dot.
	private float AddSegments(FlatFigure figure, TessellatedPath output)
	{
		var points = _points.Span.Slice(figure.First, figure.Count);
		var corners = _corners.Span.Slice(figure.First, figure.Count);
		var arc = 0f;
		var count = figure.Closed ? points.Length : points.Length - 1;
		for (var i = 0; i < count; i++)
		{
			var a = points[i];
			var b = points[(i + 1) % points.Length];
			var length = Vector2.Distance(a, b);
			if (length <= 0)
				continue;
			output.Segments.Add(new TessellatedPath.Segment(a, (b - a) / length, length, arc));
			_segmentCorners.Add(corners[i]);
			arc += length;
		}
		if (arc == 0)
		{
			output.Segments.Add(new TessellatedPath.Segment(points[0], Vector2.UnitX, 0, 0));
			_segmentCorners.Add(true);
		}
		return arc;
	}

	// The figure-local point (segment index, distance along it) at arc length r of a run, which past the figure's length
	// continues round a closed figure. `endward` picks the segment ending at a vertex rather than the one starting there.
	private static (int Segment, float X) Locate(TessellatedPath output, in FigureRuns figure, float r, bool endward)
	{
		var segments = output.Segments.Span.Slice(figure.FirstSegment, figure.SegmentCount);
		if (figure.Closed && figure.Length > 0)
		{
			r %= figure.Length;
			if (endward && r <= 0)
				r = figure.Length;
		}
		var index = segments.Length - 1;
		for (var i = 0; i < segments.Length; i++)
		{
			var end = segments[i].Arc + segments[i].Length;
			if (endward ? r <= end : r < end)
			{
				index = i;
				break;
			}
		}
		return (figure.FirstSegment + index, r - segments[index].Arc);
	}

	/// <summary>
	/// Add one run of stroke from run coordinate <paramref name="r0"/> to <paramref name="r1"/>: its start cap, its
	/// segment bodies with a join between each, and its end cap; or, for a loop (a whole closed figure), its bodies, joins
	/// and the closing join. Returns the bodies added.
	/// </summary>
	private int AddRun(TessellatedPath output, CompositionStrokeStyle style, in FigureRuns figure, float r0, float r1, bool loop,
		PenLineCap startCap, PenLineCap endCap)
	{
		var h = output.HalfThickness;
		var m = output.Margin;
		var tolerance = output.Tolerance;
		var firstPiece = output.Pieces.Count;
		var bodies = 0;

		if (r1 <= r0)
		{
			// A dot: the two caps back to back, facing along the path (or along x where the path has no length).
			var (si, x) = Locate(output, figure, r0, endward: false);
			var s = output.Segments.Span[si];
			var arc = figure.Offset + s.Arc + x;
			AddDotCap(output, startCap, s, x, atStart: true, h, m, tolerance, arc);
			AddDotCap(output, endCap, s, x, atStart: false, h, m, tolerance, arc);
		}
		else
		{
			var (startSegment, startX) = Locate(output, figure, r0, endward: false);
			if (!loop)
				AddCap(output, startCap, startSegment, startX, atStart: true, figure, r0);

			// Walk the segments from r0 to r1, joining each to the one before.
			var segments = output.Segments.Span;
			var index = startSegment;
			var x0 = startX;
			var r = r0;
			var last = figure.FirstSegment + figure.SegmentCount - 1;
			var firstBody = true;
			while (r < r1 - 1e-6f)
			{
				var s = segments[index];
				var x1 = MathF.Min(s.Length, x0 + (r1 - r));
				var rEnd = r + (x1 - x0);
				var lastBody = rEnd >= r1 - 1e-6f;
				var startClip = firstBody && !loop ? CapClip(startCap, x0, -1, h) : null;
				var endClip = lastBody && !loop ? CapClip(endCap, x1, 1, h) : null;
				var first = output.Triangles.Count;
				StrokePrimitives.Body(output.Triangles, s.Start, s.Direction, x0, x1, h, m, startClip, endClip, figure.Offset + s.Arc);
				output.Pieces.Add(new TessellatedPath.Piece(TessellatedPath.PieceKind.Body, r, rEnd, first, output.Triangles.Count - first,
					index, x0, startClip is not null, endClip is not null));
				bodies++;
				firstBody = false;
				r = rEnd;
				if (lastBody)
					break;

				// On to the next segment, round a closed figure's start if need be, through the join between them.
				var next = index == last ? figure.FirstSegment : index + 1;
				var vertex = segments[next];
				AddJoin(output, style, TessellatedPath.PieceKind.Join, CornerAt(next), vertex.Start, s.Direction, vertex.Direction,
					figure.Offset + vertex.Arc, r);
				index = next;
				x0 = 0;
			}

			if (loop)
			{
				var s0 = segments[figure.FirstSegment];
				AddJoin(output, style, TessellatedPath.PieceKind.ClosingJoin, true, s0.Start, segments[last].Direction, s0.Direction,
					figure.Offset, r0);
			}
			else
			{
				var (endSegment, endX) = Locate(output, figure, r1, endward: true);
				AddCap(output, endCap, endSegment, endX, atStart: false, figure, r1);
			}
		}

		output.Runs.Add(new TessellatedPath.Run(figure.FirstSegment, figure.SegmentCount, figure.Length, figure.Offset, figure.Closed,
			loop, r0, r1, startCap, endCap, firstPiece, output.Pieces.Count - firstPiece));
		return bodies;
	}

	// Whether the vertex where `segment` starts is a corner of the original path, where the style's join applies, rather
	// than a point inside a flattened curve.
	private bool CornerAt(int segment) => _segmentCorners[segment];

	private readonly RetainedBuffer<bool> _segmentCorners = new(64);

	private void AddJoin(TessellatedPath output, CompositionStrokeStyle style, TessellatedPath.PieceKind kind, bool corner, Vector2 point,
		Vector2 t1, Vector2 t2, float arc, float r)
	{
		// At a corner the style's join applies. Inside a flattened curve the turn is the curve's own: a small one is
		// mitered, which is exact for the flattened outline, and a sharp one (a cusp) rounded, as the curve's offset is.
		var join = corner ? style.LineJoin
			: Vector2.Dot(t1, t2) > 0.966f ? PenLineJoin.Miter : PenLineJoin.Round;
		var first = output.Triangles.Count;
		StrokePrimitives.Join(output.Triangles, join, point, t1, t2, output.HalfThickness, output.Margin, style.MiterLimit, output.Tolerance, arc);
		output.Pieces.Add(new TessellatedPath.Piece(kind, r, r, first, output.Triangles.Count - first, -1, 0, false, false));
	}

	private static void AddCap(TessellatedPath output, PenLineCap cap, int segmentIndex, float x, bool atStart, in FigureRuns figure, float r)
	{
		var h = output.HalfThickness;
		var m = output.Margin;
		var s = output.Segments.Span[segmentIndex];
		var arc = figure.Offset + s.Arc + x;
		var first = output.Triangles.Count;
		switch (cap)
		{
			case PenLineCap.Flat or PenLineCap.Square:
				{
					var reach = cap == PenLineCap.Square ? h + m : m;
					var clip = CapClip(cap, x, atStart ? -1 : 1, h);
					if (atStart)
						StrokePrimitives.CapExtension(output.Triangles, s.Start, s.Direction, x - reach, x, h, m, clip, null, arc);
					else
						StrokePrimitives.CapExtension(output.Triangles, s.Start, s.Direction, x, x + reach, h, m, null, clip, arc);
					break;
				}
			default:
				StrokePrimitives.Cap(output.Triangles, cap, s.Start + x * s.Direction, s.Direction, atStart ? -s.Direction : s.Direction, h, m,
					output.Tolerance, arc);
				break;
		}
		output.Pieces.Add(new TessellatedPath.Piece(atStart ? TessellatedPath.PieceKind.StartCap : TessellatedPath.PieceKind.EndCap, r, r, first,
			output.Triangles.Count - first, segmentIndex, x, false, false));
	}

	// One half of a dot: a flat cap draws nothing (a zero-length flat stroke has no area), the others their shape.
	private static void AddDotCap(TessellatedPath output, PenLineCap cap, in TessellatedPath.Segment s, float x, bool atStart, float h, float m,
		float tolerance, float arc)
	{
		var first = output.Triangles.Count;
		switch (cap)
		{
			case PenLineCap.Flat:
				break;
			case PenLineCap.Square:
				if (atStart)
					StrokePrimitives.CapExtension(output.Triangles, s.Start, s.Direction, x - h - m, x, h, m, x - h, null, arc);
				else
					StrokePrimitives.CapExtension(output.Triangles, s.Start, s.Direction, x, x + h + m, h, m, null, x + h, arc);
				break;
			default:
				StrokePrimitives.Cap(output.Triangles, cap, s.Start + x * s.Direction, s.Direction, atStart ? -s.Direction : s.Direction, h, m,
					tolerance, arc);
				break;
		}
		output.Pieces.Add(new TessellatedPath.Piece(atStart ? TessellatedPath.PieceKind.StartCap : TessellatedPath.PieceKind.EndCap, 0, 0, first,
			output.Triangles.Count - first, -1, x, false, false));
	}

	// Where a flat or square cap's end line lies along its segment, for a cap facing back (-1) or forward (+1) from x;
	// null for caps drawn on their own.
	private static float? CapClip(PenLineCap cap, float x, int facing, float h) => cap switch
	{
		PenLineCap.Flat => x,
		PenLineCap.Square => x + facing * h,
		_ => null,
	};

	// Fill _pattern with the style's dash pattern in path units, an odd one repeated to make it even; false when the
	// stroke is solid.
	private bool PreparePattern(CompositionStrokeStyle style)
	{
		_pattern.Clear();
		if (!style.IsDashed)
			return false;
		var dashes = style.DashArray;
		var repeats = dashes.Length % 2 == 1 ? 2 : 1;
		for (var r = 0; r < repeats; r++)
		{
			foreach (var d in dashes)
				_pattern.Add(d * style.Thickness);
		}
		return true;
	}

	// The dashes of a figure of `length`, starting `offset` into the pattern, in figure-local arc length, merged where a
	// zero gap joins them. On a closed figure a dash crossing its start is one dash, running past `length`.
	private void DashIntervals(float length, float offset, bool closed, ref int dashCount)
	{
		_dashes.Clear();
		var pattern = _pattern.Span;
		var period = 0f;
		foreach (var d in pattern)
			period += d;

		// Find where in the pattern the figure starts.
		var position = offset % period;
		if (position < 0)
			position += period;
		var index = 0;
		while (position >= pattern[index] && position > 0)
		{
			position -= pattern[index];
			index = (index + 1) % pattern.Length;
		}

		var s = 0f;
		var remaining = pattern[index] - position;
		while (s < length || (s == length && length == 0))
		{
			var e = s + remaining;
			if (index % 2 == 0)
			{
				if (++dashCount > MaxDashes)
					throw new InvalidOperationException(
						$"The dash pattern is too fine for the path: it would draw more than {MaxDashes} dashes. Lengthen the dashes or thicken the stroke.");
				var b = MathF.Min(e, length);
				if (_dashes.Count > 0 && _dashes[_dashes.Count - 1].B >= s)
					_dashes[_dashes.Count - 1] = (_dashes[_dashes.Count - 1].A, b);
				else
					_dashes.Add((s, b));
			}
			if (length == 0)
				break;
			s = e;
			index = (index + 1) % pattern.Length;
			remaining = pattern[index];
		}

		// On a closed figure, a dash at its start and one at its end are one dash through the closing join.
		if (closed && _dashes.Count > 1 && _dashes[0].A <= 0 && _dashes[_dashes.Count - 1].B >= length)
		{
			var lastStart = _dashes[_dashes.Count - 1].A;
			var firstEnd = _dashes[0].B;
			_dashes[0] = (lastStart, length + firstEnd);
			_dashes.Truncate(_dashes.Count - 1);
		}
	}
}
