using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Drawing;

/// <summary>
/// A tessellated fill or stroke: triangles with three vertices each, never shared, whose corners carry the distances the
/// vector prim antialiases with and their arc length. It is retained: tessellating into it again reuses its storage, so
/// a path that keeps its size stops allocating, and drawing it, moved, recoloured or trimmed, allocates nothing.
/// <para>
/// A stroke keeps its triangles in pieces ordered along its length: caps, segment bodies and joins. <see cref="Emit{TSink}(ref TSink, PathTrim)"/>
/// draws a trimmed stroke from those pieces: the ones inside the trim as they are, the two segments it cuts rebuilt to
/// the trim points, and new caps there, so a draw-on animation never re-tessellates.
/// </para>
/// </summary>
public sealed class TessellatedPath
{
	internal enum PieceKind : byte { Body, Join, StartCap, EndCap, ClosingJoin }

	/// <summary>One piece of a stroke: its run-coordinate range (a single point for a join or cap), its vertices, and
	/// for a body its segment, where along the segment it starts, and whether it carries its run's flat or square cap
	/// clip.</summary>
	internal readonly record struct Piece(PieceKind Kind, float R0, float R1, int FirstVertex, int VertexCount, int Segment, float X0,
		bool StartClip, bool EndClip);

	/// <summary>One straight segment of a flattened figure: where it starts, its unit direction, its length, and the
	/// figure-local arc length at its start.</summary>
	internal readonly record struct Segment(Vector2 Start, Vector2 Direction, float Length, float Arc);

	/// <summary>
	/// One continuous stretch of stroke: a whole open figure, a whole closed figure (a loop), or one dash. Its range is
	/// in run coordinates, the figure's arc length, which runs past the figure's length when a dash crosses a closed
	/// figure's start.
	/// </summary>
	internal readonly record struct Run(int FirstSegment, int SegmentCount, float FigureLength, float FigureOffset, bool ClosedFigure,
		bool Loop, float R0, float R1, PenLineCap StartCap, PenLineCap EndCap, int FirstPiece, int PieceCount);

	private const float Epsilon = 1e-5f;

	internal readonly RetainedBuffer<VectorVertex> Triangles = new(96);
	internal readonly RetainedBuffer<Piece> Pieces = new(16);
	internal readonly RetainedBuffer<Run> Runs = new(4);
	internal readonly RetainedBuffer<Segment> Segments = new(16);
	private readonly RetainedBuffer<VectorVertex> _rebuilt = new(48);
	private Rect _bounds;

	/// <summary>The triangles, three vertices each, in the path's local coordinates.</summary>
	public ReadOnlySpan<VectorVertex> Vertices => Triangles.Span;

	public int TriangleCount => Triangles.Count / 3;

	/// <summary>Whether this is a stroke rather than a fill.</summary>
	public bool IsStroke { get; private set; }

	/// <summary>A stroke's length along all its figures, which trim fractions are of; 0 for a fill.</summary>
	public float Length { get; private set; }

	/// <summary>
	/// How many straight segment bodies a stroke has. A stroke of one segment never overlaps itself; one of more can, at
	/// its joins and wherever it crosses itself, which matters for a translucent stroke.
	/// </summary>
	public int SegmentCount { get; private set; }

	/// <summary>The bounds of the untrimmed triangles, antialiasing margins included.</summary>
	public Rect Bounds => _bounds;

	/// <summary>Advances every time the path is tessellated into this object.</summary>
	public uint Version { get; private set; }

	// What the triangles were built from: the path and style objects and their versions (two paths can share a version
	// number), the transform the path's points went through, and the scale bucket.
	private CompositionPath? _sourcePath;
	private CompositionStrokeStyle? _sourceStyle;
	private (uint Path, Matrix3x2 Transform, uint Style, TessellationScale Scale) _source;

	/// <summary>Whether the triangles were built from this version of <paramref name="path"/> and
	/// <paramref name="style"/> (null for a fill) at this scale bucket, so drawing it needs no re-tessellation.</summary>
	public bool IsCurrentFor(CompositionPath path, CompositionStrokeStyle? style, TessellationScale scale) =>
		IsCurrentFor(path, Matrix3x2.Identity, style, scale);

	/// <summary>Whether the triangles were built from this version of <paramref name="path"/>, mapped through
	/// <paramref name="pathTransform"/>, and of <paramref name="style"/> (null for a fill) at this scale bucket, so drawing
	/// it needs no re-tessellation.</summary>
	public bool IsCurrentFor(CompositionPath path, in Matrix3x2 pathTransform, CompositionStrokeStyle? style, TessellationScale scale) =>
		Version != 0 && ReferenceEquals(_sourcePath, path) && ReferenceEquals(_sourceStyle, style)
		&& _source == (path.Version, pathTransform, style?.Version ?? 0, scale);

	// What rebuilding a cut piece needs: the stroke's half thickness, margin and tolerance, and the caps a trim takes.
	internal float HalfThickness;
	internal float Margin;
	internal float Tolerance;
	internal PenLineCap TrimStartCap;
	internal PenLineCap TrimEndCap;

	internal void Begin(bool isStroke, CompositionPath path, in Matrix3x2 pathTransform, CompositionStrokeStyle? style, TessellationScale scale)
	{
		Triangles.Clear();
		Pieces.Clear();
		Runs.Clear();
		Segments.Clear();
		IsStroke = isStroke;
		Length = 0;
		SegmentCount = 0;
		_sourcePath = path;
		_sourceStyle = style;
		_source = (path.Version, pathTransform, style?.Version ?? 0, scale);
	}

	internal void Finish(float length, int segmentCount)
	{
		Length = length;
		SegmentCount = segmentCount;
		var vertices = Triangles.Span;
		if (vertices.Length == 0)
			_bounds = Rect.Zero;
		else
		{
			var min = new Vector2(float.MaxValue);
			var max = new Vector2(float.MinValue);
			foreach (ref readonly var v in vertices)
			{
				min = Vector2.Min(min, v.Position);
				max = Vector2.Max(max, v.Position);
			}
			_bounds = new Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y);
		}
		Version++;
	}

	/// <summary>Draw every triangle into <paramref name="sink"/>.</summary>
	public void Emit<TSink>(ref TSink sink) where TSink : IVectorSink
	{
		if (Triangles.Count > 0)
			sink.AddTriangles(Triangles.Span);
	}

	/// <summary>
	/// Draw the part of a stroke <paramref name="trim"/> leaves into <paramref name="sink"/>: the pieces inside it as
	/// they are, the segments it cuts rebuilt to the cut, and caps where it cuts (the start cap at the start of each
	/// visible part, the end cap at its end). A fill ignores the trim. Allocation-free.
	/// </summary>
	public void Emit<TSink>(ref TSink sink, PathTrim trim) where TSink : IVectorSink
	{
		if (!IsStroke || trim.IsNone || Length <= 0)
		{
			Emit(ref sink);
			return;
		}

		var count = trim.Visible(out var first, out var second);
		if (count == 0)
			return;
		Span<(float From, float To)> visible = [(first.From * Length, first.To * Length), (second.From * Length, second.To * Length)];
		var emitter = new Emitter<TSink>(this, ref sink);
		foreach (ref readonly var run in Runs.Span)
			emitter.Run(run, visible[..count]);
		emitter.Flush();
	}

	/// <summary>The run-coordinate parts of <paramref name="run"/> that fall in the visible arc-length intervals, merged
	/// where they touch and in order. Returns how many were written to <paramref name="parts"/>.</summary>
	private static int VisibleParts(in Run run, ReadOnlySpan<(float From, float To)> visible, Span<(float A, float B)> parts)
	{
		var n = 0;
		var off = run.FigureOffset;
		var length = run.FigureLength;
		foreach (var (from, to) in visible)
		{
			// The run's first lap maps to off + r; a dash that crosses a closed figure's start continues past r = length,
			// where its arc length starts again from the figure's start.
			Add(ref n, parts, run, Math.Max(run.R0, from - off), Math.Min(Math.Min(run.R1, run.ClosedFigure ? length : run.R1), to - off));
			if (run.ClosedFigure && run.R1 > length)
				Add(ref n, parts, run, Math.Max(length, from - off + length), Math.Min(run.R1, to - off + length));
		}

		// Sort (there are at most four) and merge parts that touch.
		for (var i = 1; i < n; i++)
		{
			for (var j = i; j > 0 && parts[j].A < parts[j - 1].A; j--)
				(parts[j], parts[j - 1]) = (parts[j - 1], parts[j]);
		}
		var merged = 0;
		for (var i = 0; i < n; i++)
		{
			if (merged > 0 && parts[i].A <= parts[merged - 1].B + Epsilon)
				parts[merged - 1].B = Math.Max(parts[merged - 1].B, parts[i].B);
			else
				parts[merged++] = parts[i];
		}
		return merged;

		static void Add(ref int n, Span<(float A, float B)> parts, in Run run, float a, float b)
		{
			// A dot (a zero-length run) is visible when the trim reaches it; anything else needs some length.
			var dot = run.R0 == run.R1;
			if (dot ? a <= b + Epsilon : b > a + Epsilon)
				parts[n++] = dot ? (run.R0, run.R0) : (a, b);
		}
	}

	private enum End { Original, Trim, Through }

	/// <summary>Copies cached pieces to the sink, coalescing neighbours into one call, and rebuilds cut pieces into
	/// retained scratch.</summary>
	private ref struct Emitter<TSink> where TSink : IVectorSink
	{
		private readonly TessellatedPath _path;
		private ref TSink _sink;
		private int _pendingStart;
		private int _pendingCount;

		public Emitter(TessellatedPath path, ref TSink sink)
		{
			_path = path;
			_sink = ref sink;
			_path._rebuilt.Clear();
		}

		public void Run(in TessellatedPath.Run run, scoped ReadOnlySpan<(float From, float To)> visible)
		{
			Span<(float A, float B)> parts = stackalloc (float, float)[4];
			var n = VisibleParts(run, visible, parts);
			if (n == 0)
				return;

			if (run.R0 == run.R1)
			{
				CopyRange(run.FirstPiece, run.PieceCount);
				return;
			}

			if (!run.Loop)
			{
				for (var i = 0; i < n; i++)
					Span(run, parts[i].A, parts[i].B, parts[i].A <= run.R0 + Epsilon ? End.Original : End.Trim,
						parts[i].B >= run.R1 - Epsilon ? End.Original : End.Trim);
				return;
			}

			// A closed figure: a part reaching both its start and its end continues through the closing join.
			var whole = n == 1 && parts[0].A <= run.R0 + Epsilon && parts[0].B >= run.R1 - Epsilon;
			var wraps = n > 1 && parts[0].A <= run.R0 + Epsilon && parts[n - 1].B >= run.R1 - Epsilon;
			if (whole)
			{
				Span(run, run.R0, run.R1, End.Through, End.Through);
				CopyClosingJoin(run);
				return;
			}
			for (var i = 0; i < n; i++)
			{
				var start = wraps && i == 0 ? End.Through : End.Trim;
				var end = wraps && i == n - 1 ? End.Through : End.Trim;
				Span(run, parts[i].A, parts[i].B, start, end);
			}
			if (wraps)
				CopyClosingJoin(run);
		}

		// The pieces of `run` between run coordinates u and v, with the given ends.
		private void Span(in TessellatedPath.Run run, float u, float v, End start, End end)
		{
			var pieces = _path.Pieces.Span;
			var h = _path.HalfThickness;
			var m = _path.Margin;
			(int Segment, float X) startAt = (-1, 0), endAt = (-1, 0);
			for (var i = run.FirstPiece; i < run.FirstPiece + run.PieceCount; i++)
			{
				ref readonly var p = ref pieces[i];
				switch (p.Kind)
				{
					case PieceKind.StartCap:
						if (start == End.Original)
							Copy(p);
						break;
					case PieceKind.EndCap:
						if (end == End.Original)
							Copy(p);
						break;
					case PieceKind.Join:
						if (p.R0 > u + Epsilon && p.R0 < v - Epsilon)
							Copy(p);
						break;
					case PieceKind.Body:
						{
							var a = Math.Max(u, p.R0);
							var b = Math.Min(v, p.R1);
							if (b <= a + Epsilon)
								break;
							var trimStart = start == End.Trim && a <= u + Epsilon;
							var trimEnd = end == End.Trim && b >= v - Epsilon;
							var segment = _path.Segments.Span[p.Segment];
							float xa = p.X0 + (a - p.R0), xb = p.X0 + (b - p.R0);
							if (trimStart)
								startAt = (p.Segment, xa);
							if (trimEnd)
								endAt = (p.Segment, xb);
							var whole = a <= p.R0 + Epsilon && b >= p.R1 - Epsilon;
							if (whole && !trimStart && !trimEnd && (!p.StartClip || start == End.Original) && (!p.EndClip || end == End.Original))
							{
								Copy(p);
								break;
							}
							var startClip = trimStart ? Clip(_path.TrimStartCap, xa, -1, h)
								: p.StartClip && start == End.Original ? Clip(run.StartCap, p.X0, -1, h) : null;
							var endClip = trimEnd ? Clip(_path.TrimEndCap, xb, 1, h)
								: p.EndClip && end == End.Original ? Clip(run.EndCap, p.X0 + (p.R1 - p.R0), 1, h) : null;
							StrokePrimitives.Body(_path._rebuilt, segment.Start, segment.Direction, xa, xb, h, m, startClip, endClip,
								run.FigureOffset + segment.Arc);
							break;
						}
				}
			}
			if (startAt.Segment >= 0)
				RebuildCap(run, _path.TrimStartCap, startAt.Segment, startAt.X, atStart: true);
			if (endAt.Segment >= 0)
				RebuildCap(run, _path.TrimEndCap, endAt.Segment, endAt.X, atStart: false);
		}

		private void RebuildCap(in TessellatedPath.Run run, PenLineCap cap, int segmentIndex, float x, bool atStart)
		{
			var segment = _path.Segments.Span[segmentIndex];
			var h = _path.HalfThickness;
			var m = _path.Margin;
			var arc = run.FigureOffset + segment.Arc + x;
			var t = segment.Direction;
			switch (cap)
			{
				case PenLineCap.Flat or PenLineCap.Square:
					{
						var reach = cap == PenLineCap.Square ? h + m : m;
						var clip = Clip(cap, x, atStart ? -1 : 1, h);
						if (atStart)
							StrokePrimitives.CapExtension(_path._rebuilt, segment.Start, t, x - reach, x, h, m, clip, null, arc);
						else
							StrokePrimitives.CapExtension(_path._rebuilt, segment.Start, t, x, x + reach, h, m, null, clip, arc);
						break;
					}
				default:
					StrokePrimitives.Cap(_path._rebuilt, cap, segment.Start + x * t, t, atStart ? -t : t, h, m, _path.Tolerance, arc);
					break;
			}
		}

		// Where a flat or square cap's end line lies along the segment, for a cap facing back (-1) or forward (+1) at x.
		private static float? Clip(PenLineCap cap, float x, int facing, float h) => cap switch
		{
			PenLineCap.Flat => x,
			PenLineCap.Square => x + facing * h,
			_ => null,
		};

		private void CopyClosingJoin(in TessellatedPath.Run run)
		{
			var pieces = _path.Pieces.Span;
			for (var i = run.FirstPiece; i < run.FirstPiece + run.PieceCount; i++)
			{
				if (pieces[i].Kind == PieceKind.ClosingJoin)
					Copy(pieces[i]);
			}
		}

		private void CopyRange(int firstPiece, int count)
		{
			var pieces = _path.Pieces.Span;
			for (var i = firstPiece; i < firstPiece + count; i++)
				Copy(pieces[i]);
		}

		private void Copy(in Piece p)
		{
			if (p.VertexCount == 0)
				return;
			if (_pendingCount > 0 && _pendingStart + _pendingCount == p.FirstVertex)
			{
				_pendingCount += p.VertexCount;
				return;
			}
			FlushPending();
			_pendingStart = p.FirstVertex;
			_pendingCount = p.VertexCount;
		}

		private void FlushPending()
		{
			if (_pendingCount > 0)
				_sink.AddTriangles(_path.Triangles.Span.Slice(_pendingStart, _pendingCount));
			_pendingCount = 0;
		}

		public void Flush()
		{
			FlushPending();
			if (_path._rebuilt.Count > 0)
				_sink.AddTriangles(_path._rebuilt.Span);
		}
	}
}
