using System.Numerics;
using SUIsei.Composition;

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
/// glyph's pieces keep their part of its atlas cell. A vector triangle's pieces keep their part of its edge distances the
/// same way, which are linear across it, so the pieces antialias exactly as the whole triangle would. Allocation-free:
/// the pieces are built on the stack.
/// </para>
/// </summary>
internal static class GradientSplitter
{
	// The most corners a polygon given here may have; cutting it to a band adds at most two.
	private const int MaxCorners = 8;
	private const int MaxCut = MaxCorners + 2;

	// The most bands one polygon is cut into: a repeating gradient whose period is far smaller than what it paints would
	// otherwise emit without bound. Past it, the rest of the polygon is one band.
	private const int MaxBands = 1 << 16;

	/// <summary>Append <paramref name="local"/>, a convex polygon with corners in order and their <paramref name="uv"/>,
	/// painted by <paramref name="gradient"/> along <paramref name="line"/>, its alpha multiplied by
	/// <paramref name="opacity"/>.</summary>
	public static void AddPolygon(UIDrawList list, ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, CompositionGradientBrush gradient,
		in GradientLine line, float opacity, in Matrix3x2 matrix, UIPrimKind kind, Vector4 @params, in Rect clip, int slot)
	{
		var n = local.Length;
		ArgumentOutOfRangeException.ThrowIfGreaterThan(n, MaxCorners);
		Span<float> t = stackalloc float[MaxCorners];
		if (!line.IsDegenerate)
		{
			for (var i = 0; i < n; i++)
				t[i] = line.T(local[i]);
		}
		AddBands(list, local, uv, default, t[..n], line.IsDegenerate, gradient, opacity, matrix, kind, @params, clip, slot);
	}

	/// <summary>
	/// Append a triangle of the <see cref="UIPrimKind.Vector"/> prim, each corner with its edge
	/// <paramref name="distances"/>, painted by <paramref name="gradient"/> at <paramref name="t"/> per corner: a gradient
	/// line's measure of its position, or how far along a stroke it lies. The pieces' corners interpolate the distances.
	/// With <paramref name="degenerate"/> the gradient has no direction and paints its last stop.
	/// </summary>
	public static void AddVectorTriangle(UIDrawList list, ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> distances,
		ReadOnlySpan<float> t, bool degenerate, CompositionGradientBrush gradient, float opacity, in Matrix3x2 matrix, in Rect clip, int slot) =>
		AddBands(list, local, uv, distances, t, degenerate, gradient, opacity, matrix, UIPrimKind.Vector, default, clip, slot);

	// Cut the polygon into bands of linear colour and append each. `custom`, when given, is each corner's own params,
	// interpolated like the uv; otherwise every corner takes `params`.
	private static void AddBands(UIDrawList list, ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> custom,
		ReadOnlySpan<float> t, bool degenerate, CompositionGradientBrush gradient, float opacity, in Matrix3x2 matrix, UIPrimKind kind,
		Vector4 @params, in Rect clip, int slot)
	{
		var n = local.Length;
		if (gradient.ColorStops.Length == 0 || clip.IsEmpty)
			return;

		Span<Vector4> colors = stackalloc Vector4[MaxCut];
		var tMin = float.PositiveInfinity;
		var tMax = float.NegativeInfinity;
		if (!degenerate)
		{
			for (var i = 0; i < n; i++)
			{
				tMin = MathF.Min(tMin, t[i]);
				tMax = MathF.Max(tMax, t[i]);
			}
		}

		// No direction to vary along (a gradient with no length, or a polygon flat across it): one colour throughout.
		if (degenerate || !(tMax > tMin))
		{
			var solid = degenerate
				? gradient.StopColor(gradient.ColorStops[^1], opacity)
				: gradient.EvaluateAbove(tMin, opacity);
			colors[..n].Fill(solid);
			Emit(list, local, uv, custom, colors[..n], matrix, kind, @params, clip, slot);
			return;
		}

		Span<Vector2> bandPos = stackalloc Vector2[MaxCut];
		Span<Vector2> bandUv = stackalloc Vector2[MaxCut];
		Span<Vector4> bandCustom = stackalloc Vector4[custom.IsEmpty ? 0 : MaxCut];
		Span<float> bandT = stackalloc float[MaxCut];

		var a = tMin;
		var colorA = gradient.EvaluateAbove(a, opacity);
		for (var bands = 0; a < tMax; bands++)
		{
			// The last band the limit allows runs to the end, interpolated straight across: a gradient repeating that finely
			// has periods far below a pixel, and drawing them wrong beats stopping the frame.
			var b = bands == MaxBands - 1 ? tMax : MathF.Min(gradient.NextBreak(a), tMax);
			if (!(b > a))
				b = MathF.BitIncrement(a); // rounding put the next break on this one: step past it
			var colorB = gradient.EvaluateBelow(b, opacity);

			var m = CutToBand(local, uv, custom, t, a, b, bandPos, bandUv, bandCustom, bandT);
			if (m >= 3)
			{
				var span = b - a;
				for (var i = 0; i < m; i++)
				{
					// a·(1−s) + b·s is exact at both ends, so a corner on a stop gets the stop's colour exactly.
					var s = Math.Clamp((bandT[i] - a) / span, 0f, 1f);
					colors[i] = colorA * (1f - s) + colorB * s;
				}
				Emit(list, bandPos[..m], bandUv[..m], custom.IsEmpty ? default : bandCustom[..m], colors[..m], matrix, kind, @params, clip, slot);
			}

			a = b;
			colorA = gradient.EvaluateAbove(b, opacity);
		}
	}

	private static void Emit(UIDrawList list, ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> custom,
		ReadOnlySpan<Vector4> colors, in Matrix3x2 matrix, UIPrimKind kind, Vector4 @params, in Rect clip, int slot)
	{
		if (custom.IsEmpty)
			list.AddPolygon(local, uv, colors, matrix, kind, @params, clip, slot);
		else
			list.AddPolygon(local, uv, colors, custom, matrix, kind, clip, slot);
	}

	/// <summary>
	/// Cut a convex polygon of at most <see cref="MaxCorners"/> corners to the box from <paramref name="min"/> to
	/// <paramref name="max"/> (a side at an infinity is open), interpolating its corners' <paramref name="custom"/> params,
	/// and return how many corners <paramref name="outPos"/> and <paramref name="outCustom"/> now hold (under 3 when
	/// nothing is left). The cuts are made as the bands are, so the triangles of a mesh cut a shared edge at the same
	/// points.
	/// </summary>
	public static int CutToBox(ReadOnlySpan<Vector2> pos, ReadOnlySpan<Vector4> custom, Vector2 min, Vector2 max,
		Span<Vector2> outPos, Span<Vector4> outCustom)
	{
		var n = pos.Length;
		ArgumentOutOfRangeException.ThrowIfGreaterThan(n, MaxCorners);
		Span<float> t = stackalloc float[MaxCut];
		Span<float> cutT = stackalloc float[MaxCut];
		Span<Vector2> midPos = stackalloc Vector2[MaxCut];
		Span<Vector4> midCustom = stackalloc Vector4[MaxCut];
		Span<Vector2> unusedUv = stackalloc Vector2[MaxCut];
		for (var i = 0; i < n; i++)
			t[i] = pos[i].X;
		var m = CutToBand(pos, pos, custom, t[..n], min.X, max.X, midPos, unusedUv, midCustom, cutT);
		if (m < 3)
			return m;
		for (var i = 0; i < m; i++)
			t[i] = midPos[i].Y;
		return CutToBand(midPos[..m], midPos[..m], midCustom[..m], t[..m], min.Y, max.Y, outPos, unusedUv, outCustom, cutT);
	}

	// The part of a convex polygon between the lines t = a and t = b, in order around it: each corner inside the band,
	// and where an edge crosses either line, the crossing. A crossing is always computed from the original edge's two
	// corners, never from a piece of it, so the band either side of a line gets bit-identical corners there and the
	// triangles meet exactly, with no pixel on the seam drawn twice or missed.
	private static int CutToBand(ReadOnlySpan<Vector2> pos, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> custom, ReadOnlySpan<float> t,
		float a, float b, Span<Vector2> outPos, Span<Vector2> outUv, Span<Vector4> outCustom, Span<float> outT)
	{
		var count = 0;
		var n = pos.Length;
		for (var i = 0; i < n; i++)
		{
			var j = i + 1 == n ? 0 : i + 1;
			if (t[i] >= a && t[i] <= b)
			{
				outPos[count] = pos[i];
				outUv[count] = uv[i];
				if (!custom.IsEmpty)
					outCustom[count] = custom[i];
				outT[count] = t[i];
				count++;
			}
			// Crossings strictly inside the edge, in the order the edge meets them.
			var (first, second) = t[j] >= t[i] ? (a, b) : (b, a);
			var lo = MathF.Min(t[i], t[j]);
			var hi = MathF.Max(t[i], t[j]);
			if (lo < first && first < hi)
				AddCrossing(ref count, pos, uv, custom, t, i, j, first, outPos, outUv, outCustom, outT);
			if (lo < second && second < hi)
				AddCrossing(ref count, pos, uv, custom, t, i, j, second, outPos, outUv, outCustom, outT);
		}
		return count;
	}

	// The point where the edge between corners i and j crosses t = line, with t set to exactly the line. It is computed
	// from the edge's lower corner (by x, then y) to its upper one whichever way the polygon runs along it, so two
	// triangles of a mesh that share the edge, one each way round, cut it at bit-identical points.
	private static void AddCrossing(ref int count, ReadOnlySpan<Vector2> pos, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> custom,
		ReadOnlySpan<float> t, int i, int j, float line, Span<Vector2> outPos, Span<Vector2> outUv, Span<Vector4> outCustom, Span<float> outT)
	{
		if (pos[j].X < pos[i].X || (pos[j].X == pos[i].X && pos[j].Y < pos[i].Y))
			(i, j) = (j, i);
		var f = (line - t[i]) / (t[j] - t[i]);
		outPos[count] = Vector2.Lerp(pos[i], pos[j], f);
		outUv[count] = Vector2.Lerp(uv[i], uv[j], f);
		if (!custom.IsEmpty)
			outCustom[count] = custom[i] == custom[j] ? custom[i] : Vector4.Lerp(custom[i], custom[j], f); // keep an unused slot exact
		outT[count] = line;
		count++;
	}
}
