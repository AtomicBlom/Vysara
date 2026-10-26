using System.Numerics;

namespace SUIsei;

/// <summary>
/// Paints convex polygons with a linear gradient exactly, with no ramp texture: the polygon is cut into bands wherever
/// the gradient's colour stops being linear along it (each stop, each repeat of a wrapping gradient, and each piece of an
/// sRGB-interpolated interval), and every corner of every band gets the gradient's colour at that point. Within a band
/// the colour is linear in position, which is exactly what interpolating vertex colours across a triangle produces, so a
/// GPU and the CPU rasterizer draw the gradient itself, colours above paper white included.
/// <para>
/// Each corner keeps its uv, interpolated where a cut crosses an edge, so a rectangle's bands keep their part of its 0..1
/// uv (the rounded-rectangle and border shading reads it, with the whole rectangle's size, to find its edges) and a
/// glyph's pieces keep their part of its atlas cell. Allocation-free: the pieces are built on the stack.
/// </para>
/// </summary>
internal static class GradientSplitter
{
	// The most corners a polygon given here may have; cutting it to a band adds at most two.
	private const int MaxCorners = 8;
	private const int MaxCut = MaxCorners + 2;

	// The most bands one polygon is cut into: a repeating gradient whose period is far smaller than what it paints would
	// otherwise emit without bound.
	private const int MaxBands = 1 << 16;

	/// <summary>Append <paramref name="local"/>, a convex polygon with corners in order and their <paramref name="uv"/>,
	/// painted by <paramref name="gradient"/> along <paramref name="line"/>, its alpha multiplied by
	/// <paramref name="opacity"/>.</summary>
	public static void AddPolygon(UIDrawList list, ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, CompositionGradientBrush gradient,
		in GradientLine line, float opacity, in Matrix3x2 matrix, UIPrimKind kind, Vector4 @params, in Rect clip, int slot)
	{
		var n = local.Length;
		ArgumentOutOfRangeException.ThrowIfGreaterThan(n, MaxCorners);
		if (gradient.ColorStops.Length == 0 || clip.IsEmpty)
			return;

		Span<Vector4> colors = stackalloc Vector4[MaxCut];
		Span<float> t = stackalloc float[MaxCorners];
		var tMin = float.PositiveInfinity;
		var tMax = float.NegativeInfinity;
		if (!line.IsDegenerate)
		{
			for (var i = 0; i < n; i++)
			{
				t[i] = line.T(local[i]);
				tMin = MathF.Min(tMin, t[i]);
				tMax = MathF.Max(tMax, t[i]);
			}
		}

		// No direction to vary along (a gradient with no length, or a polygon flat across it): one colour throughout.
		if (line.IsDegenerate || !(tMax > tMin))
		{
			var solid = line.IsDegenerate
				? gradient.ColorStops[^1].Color.ToPremultipliedVector(opacity)
				: gradient.EvaluateAbove(tMin, opacity);
			colors[..n].Fill(solid);
			list.AddPolygon(local, uv, colors[..n], matrix, kind, @params, clip, slot);
			return;
		}

		Span<Vector2> abovePos = stackalloc Vector2[MaxCut];
		Span<Vector2> aboveUv = stackalloc Vector2[MaxCut];
		Span<float> aboveT = stackalloc float[MaxCut];
		Span<Vector2> bandPos = stackalloc Vector2[MaxCut];
		Span<Vector2> bandUv = stackalloc Vector2[MaxCut];
		Span<float> bandT = stackalloc float[MaxCut];

		var a = tMin;
		var colorA = gradient.EvaluateAbove(a, opacity);
		for (var bands = 0; a < tMax; bands++)
		{
			if (bands == MaxBands)
				throw new InvalidOperationException(
					$"A gradient repeats more than {MaxBands} times across what it paints; make its start and end further apart.");

			var b = MathF.Min(gradient.NextBreak(a), tMax);
			if (!(b > a))
				b = MathF.BitIncrement(a); // rounding put the next break on this one: step past it
			var colorB = gradient.EvaluateBelow(b, opacity);

			var m = Cut(local, uv, t[..n], a, keepAbove: true, abovePos, aboveUv, aboveT);
			m = Cut(abovePos[..m], aboveUv[..m], aboveT[..m], b, keepAbove: false, bandPos, bandUv, bandT);
			if (m >= 3)
			{
				var span = b - a;
				for (var i = 0; i < m; i++)
				{
					// a·(1−s) + b·s is exact at both ends, so a corner on a stop gets the stop's colour exactly.
					var s = Math.Clamp((bandT[i] - a) / span, 0f, 1f);
					colors[i] = colorA * (1f - s) + colorB * s;
				}
				list.AddPolygon(bandPos[..m], bandUv[..m], colors[..m], matrix, kind, @params, clip, slot);
			}

			a = b;
			colorA = gradient.EvaluateAbove(b, opacity);
		}
	}

	// Cut a convex polygon by the line t = edge (Sutherland–Hodgman), keeping the side where t ≥ edge (keepAbove) or
	// t ≤ edge. A corner the line crosses an edge at gets t = edge exactly; one already on the line is kept once.
	private static int Cut(ReadOnlySpan<Vector2> pos, ReadOnlySpan<Vector2> uv, ReadOnlySpan<float> t, float edge, bool keepAbove,
		Span<Vector2> outPos, Span<Vector2> outUv, Span<float> outT)
	{
		var count = 0;
		var n = pos.Length;
		for (var i = 0; i < n; i++)
		{
			var j = i + 1 == n ? 0 : i + 1;
			var inI = keepAbove ? t[i] >= edge : t[i] <= edge;
			var inJ = keepAbove ? t[j] >= edge : t[j] <= edge;
			if (inI)
			{
				outPos[count] = pos[i];
				outUv[count] = uv[i];
				outT[count] = t[i];
				count++;
			}
			if (inI == inJ || t[i] == edge || t[j] == edge)
				continue;
			var f = (edge - t[i]) / (t[j] - t[i]);
			outPos[count] = Vector2.Lerp(pos[i], pos[j], f);
			outUv[count] = Vector2.Lerp(uv[i], uv[j], f);
			outT[count] = edge;
			count++;
		}
		return count;
	}
}
