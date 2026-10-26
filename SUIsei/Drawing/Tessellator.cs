using System.Numerics;

namespace SUIsei.Drawing;

/// <summary>
/// Turns a <see cref="CompositionPath"/> into antialiased triangles for the vector prim: a fill, or a stroke with WinUI's
/// joins, caps and dashes. Curves are flattened to within a quarter pixel at the <see cref="TessellationScale"/>; every
/// edge the result shows carries a signed distance, so a renderer antialiases it exactly at any position, rotation or
/// scale within the bucket.
/// <para>
/// Public and dependency-free, so a foreign compositor can tessellate the composition's paths itself. It keeps scratch
/// buffers between calls and writes into a retained <see cref="TessellatedPath"/>, so re-tessellating paths of a
/// steady size allocates nothing. One tessellator is not safe to use from two threads at once.
/// </para>
/// </summary>
public sealed partial class Tessellator
{
	/// <summary>One flattened figure: a range of <see cref="_points"/>, whether it is closed, whether it is filled, and
	/// whether it draws at all (a lone move does not).</summary>
	private readonly record struct FlatFigure(int First, int Count, bool Closed, bool Filled, bool Draws);

	// Flattened figures: points with a flag saying whether each is a corner of the original path (where the stroke's line
	// join applies) rather than a point inside a flattened curve.
	private readonly RetainedBuffer<Vector2> _points = new(64);
	private readonly RetainedBuffer<bool> _corners = new(64);
	private readonly RetainedBuffer<FlatFigure> _figures = new(4);

	/// <summary>The most curve segments one Bézier is flattened into.</summary>
	private const int MaxCurveSegments = 4096;

	// Flatten every figure of `path` to within `tolerance`, dropping points that repeat the one before.
	private void Flatten(CompositionPath path, float tolerance)
	{
		_points.Clear();
		_corners.Clear();
		_figures.Clear();
		var verbs = path.Verbs;
		var points = path.Points;
		for (var f = 0; f < path.FigureCount; f++)
		{
			var info = path.GetFigure(f);
			var first = _points.Count;
			var p = info.FirstPoint;
			var current = Vector2.Zero;
			var draws = false;
			foreach (var verb in verbs.Slice(info.FirstVerb, info.VerbCount))
			{
				switch (verb)
				{
					case PathVerb.Move:
						current = points[p++];
						Add(current, true);
						break;
					case PathVerb.Line:
						current = points[p++];
						Add(current, true);
						draws = true;
						break;
					case PathVerb.Quadratic:
						{
							var (c, e) = (points[p], points[p + 1]);
							p += 2;
							var n = Steps(2f * (current - 2f * c + e).Length(), tolerance);
							for (var i = 1; i < n; i++)
							{
								var t = (float)i / n;
								var u = 1f - t;
								Add(u * u * current + 2f * u * t * c + t * t * e, false);
							}
							Add(e, true);
							current = e;
							draws = true;
							break;
						}
					case PathVerb.Cubic:
						{
							var (c1, c2, e) = (points[p], points[p + 1], points[p + 2]);
							p += 3;
							var bend = MathF.Max((current - 2f * c1 + c2).Length(), (c1 - 2f * c2 + e).Length());
							var n = Steps(6f * bend, tolerance);
							for (var i = 1; i < n; i++)
							{
								var t = (float)i / n;
								var u = 1f - t;
								Add(u * u * u * current + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * e, false);
							}
							Add(e, true);
							current = e;
							draws = true;
							break;
						}
				}
			}

			var count = _points.Count - first;
			// A closed figure's return to its start is its closing segment, not a point of its own.
			if (info.IsClosed && count > 1 && Near(_points.Span[first + count - 1], _points.Span[first]))
				count--;
			_figures.Add(new FlatFigure(first, count, info.IsClosed, info.IsFilled, draws));
			Truncate(first + count);

			void Add(Vector2 point, bool corner)
			{
				if (_points.Count > first && Near(_points.Span[^1], point))
				{
					if (corner)
						_corners[_corners.Count - 1] = true;
					return;
				}
				_points.Add(point);
				_corners.Add(corner);
			}
		}
	}

	// The segments to flatten a curve whose second derivative is at most `bend` into, so it strays from its chords by at
	// most `tolerance` (a uniform split strays by bend / (8 n²)).
	private static int Steps(float bend, float tolerance) =>
		Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(bend / (8f * tolerance))), 1, MaxCurveSegments);

	private static bool Near(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) <= 1e-12f * MathF.Max(1f, a.LengthSquared());

	private void Truncate(int count)
	{
		_points.Truncate(count);
		_corners.Truncate(count);
	}
}
