using System.Threading.Tasks;
using SUIsei.Text.Geometry;

namespace SUIsei.Text.Generation;

/// <summary>
/// Maps a cell texel to a point in the glyph's own (font-unit, y-up) space: <c>font = (boxLeft + (px+0.5)·s,
/// boxTop − (py+0.5)·s)</c> where <c>s</c> is font-units-per-texel. Distances come out in font units and are scaled
/// to the texel distance range at quantization time, so no per-glyph shape transform is needed.
/// </summary>
internal readonly record struct CellProjection(double BoxLeft, double BoxTop, double UnitsPerTexel)
{
	public Vector2D Project(double px, double py) =>
		new(BoxLeft + (px + 0.5) * UnitsPerTexel, BoxTop - (py + 0.5) * UnitsPerTexel);
}

/// <summary>
/// The multi-threaded software MTSDF rasterizer (msdfgen algorithm). Each contour is sampled into its own
/// per-channel selector and the results are merged with a winding-aware overlapping-contour combiner, so glyphs
/// built from overlapping shapes (e.g. a stem plus separate arm rectangles) don't get false interior edges. Rows
/// can be generated in parallel; the baker disables row parallelism when it already parallelizes over glyphs.
/// </summary>
internal static class MsdfGenerator
{
	public static GlyphBitmap Generate(
		Shape shape, int width, int height, double rangeUnits, double pxRangeTexels, CellProjection projection, bool parallelRows)
	{
		var bitmap = new GlyphBitmap(width, height);
		var pixels = bitmap.Pixels;
		var invRange = 1.0 / rangeUnits;
		var windings = ComputeWindings(shape);

		if (parallelRows)
			Parallel.For(0, height, py => GenerateRow(shape, windings, pixels, width, py, invRange, projection));
		else
			for (var py = 0; py < height; py++)
				GenerateRow(shape, windings, pixels, width, py, invRange, projection);

		CorrectPolarity(shape, bitmap, projection);
		ErrorCorrect(shape, bitmap, projection);
		CorrectInterpolation(shape, bitmap, projection, pxRangeTexels);
		return bitmap;
	}

	/// <summary>+1 for a clockwise (filled, in TrueType y-up) contour, −1 for counter-clockwise (a hole).</summary>
	private static int[] ComputeWindings(Shape shape)
	{
		var windings = new int[shape.Contours.Count];
		for (var i = 0; i < shape.Contours.Count; i++)
		{
			var area = 0.0;
			foreach (var edge in shape.Contours[i].Edges)
				area += Vector2D.Cross(edge.Point(0), edge.Point(1));
			windings[i] = area < 0 ? 1 : area > 0 ? -1 : 0;
		}

		return windings;
	}

	private static void GenerateRow(
		Shape shape, int[] windings, float[] pixels, int width, int py, double invRange, CellProjection projection)
	{
		var samplers = new MultiDistanceSampler[shape.Contours.Count];
		var rowBase = py * width * 4;
		for (var px = 0; px < width; px++)
		{
			var p = projection.Project(px, py);
			var value = Combine(shape, windings, samplers, p, invRange);
			var o = rowBase + px * 4;
			pixels[o] = value.R;
			pixels[o + 1] = value.G;
			pixels[o + 2] = value.B;
			pixels[o + 3] = value.A;
		}
	}

	private static (float R, float G, float B, float A) Combine(
		Shape shape, int[] windings, MultiDistanceSampler[] samplers, Vector2D p, double invRange)
	{
		var n = shape.Contours.Count;
		for (var i = 0; i < n; i++)
		{
			var sampler = new MultiDistanceSampler();
			var edges = shape.Contours[i].Edges;
			var m = edges.Count;
			for (var j = 0; j < m; j++)
				sampler.AddEdge(edges[(j - 1 + m) % m], edges[j], edges[(j + 1) % m], p);
			samplers[i] = sampler;
		}

		if (n == 1)
			return samplers[0].Resolve(p, invRange);

		// msdfgen's overlapping-contour combiner: merge contours by winding, then pick the contour responsible
		// for the nearest boundary of the union/difference so internal (overlapped) edges don't leak through.
		var shapeSel = new MultiDistanceSampler();
		var innerSel = new MultiDistanceSampler();
		var outerSel = new MultiDistanceSampler();
		for (var i = 0; i < n; i++)
		{
			var med = samplers[i].MedianDistance(p);
			shapeSel.Merge(samplers[i]);
			if (windings[i] > 0 && med >= 0)
				innerSel.Merge(samplers[i]);
			if (windings[i] < 0 && med <= 0)
				outerSel.Merge(samplers[i]);
		}

		var shapeMed = shapeSel.MedianDistance(p);
		var innerMed = innerSel.MedianDistance(p);
		var outerMed = outerSel.MedianDistance(p);

		MultiDistanceSampler chosen;
		double chosenMed;
		int winding;

		if (innerMed >= 0 && Math.Abs(innerMed) <= Math.Abs(outerMed))
		{
			chosen = innerSel;
			chosenMed = innerMed;
			winding = 1;
			for (var i = 0; i < n; i++)
			{
				if (windings[i] <= 0)
					continue;
				var cd = samplers[i].MedianDistance(p);
				if (Math.Abs(cd) < Math.Abs(outerMed) && cd > chosenMed)
				{
					chosen = samplers[i];
					chosenMed = cd;
				}
			}
		}
		else if (outerMed <= 0 && Math.Abs(outerMed) <= Math.Abs(innerMed))
		{
			chosen = outerSel;
			chosenMed = outerMed;
			winding = -1;
			for (var i = 0; i < n; i++)
			{
				if (windings[i] >= 0)
					continue;
				var cd = samplers[i].MedianDistance(p);
				if (Math.Abs(cd) < Math.Abs(innerMed) && cd < chosenMed)
				{
					chosen = samplers[i];
					chosenMed = cd;
				}
			}
		}
		else
		{
			return shapeSel.Resolve(p, invRange);
		}

		for (var i = 0; i < n; i++)
		{
			if (windings[i] == winding)
				continue;
			var cd = samplers[i].MedianDistance(p);
			if (cd * chosenMed >= 0 && Math.Abs(cd) < Math.Abs(chosenMed))
			{
				chosen = samplers[i];
				chosenMed = cd;
			}
		}

		if (chosenMed == shapeMed)
			chosen = shapeSel;
		return chosen.Resolve(p, invRange);
	}

	/// <summary>
	/// Force "inside ⇒ value &gt; 0.5". The per-edge sign follows the font's contour winding (CW-outer for TrueType,
	/// CCW-outer for CFF), so the overall polarity can come out inverted. Vote median-sign vs the winding-rule
	/// inside/outside test at clear (non-edge) sample texels and flip the whole cell if the majority disagree.
	/// </summary>
	private static void CorrectPolarity(Shape shape, GlyphBitmap bitmap, CellProjection projection)
	{
		var pixels = bitmap.Pixels;
		var mismatches = 0;
		var matches = 0;
		var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 16);

		for (var py = step / 2; py < bitmap.Height; py += step)
		{
			for (var px = step / 2; px < bitmap.Width; px += step)
			{
				var o = (py * bitmap.Width + px) * 4;
				var median = Median(pixels[o], pixels[o + 1], pixels[o + 2]);
				if (Math.Abs(median - 0.5f) < 0.25f) // skip the antialiased boundary band
					continue;
				var insideByField = median > 0.5f;
				var insideByWinding = shape.WindingAt(projection.Project(px, py)) != 0;
				if (insideByField == insideByWinding)
					matches++;
				else
					mismatches++;
			}
		}

		if (mismatches > matches)
			for (var i = 0; i < pixels.Length; i++)
				pixels[i] = 1f - pixels[i];
	}

	/// <summary>
	/// Error correction against the glyph's <em>exact</em> fill (the scanline nonzero-winding test) rather than the
	/// reconstructed alpha — because the overlapping-contour combiner can produce a wrong-signed <em>true</em> distance
	/// in a thin region near a sharp concave/reversal corner (e.g. the inner "beak" of Cascadia's <c>|&gt;</c> ring),
	/// where the field reports outside well inside the ink. An accurate field's sign at a texel centre always matches
	/// the exact fill there, so a disagreement marks an artifact: reflect the true-distance channel across 0.5 to the
	/// correct side (which preserves its antialiasing magnitude) and collapse RGB onto it. Genuine edges and sharp
	/// corners agree in sign and are left untouched, so multi-channel sharpening survives everywhere it is real.
	/// </summary>
	private static void ErrorCorrect(Shape shape, GlyphBitmap bitmap, CellProjection projection)
	{
		var px = bitmap.Pixels;
		for (var py = 0; py < bitmap.Height; py++)
		{
			for (var pxi = 0; pxi < bitmap.Width; pxi++)
			{
				var o = (py * bitmap.Width + pxi) * 4;
				var insideExact = shape.WindingAt(projection.Project(pxi, py)) != 0;

				// Reflect the true-distance channel to the exact-fill side (1 - v flips across 0.5, keeping |v - 0.5|).
				if (px[o + 3] > 0.5f != insideExact)
					px[o + 3] = 1f - px[o + 3];

				var median = Median(px[o], px[o + 1], px[o + 2]);
				if (median > 0.5f != insideExact)
				{
					var trueValue = px[o + 3];
					px[o] = trueValue;
					px[o + 1] = trueValue;
					px[o + 2] = trueValue;
				}
			}
		}
	}

	/// <summary>
	/// Error correction between texels, where a renderer actually samples. A texel can be right at its centre yet clash with
	/// a neighbour: where overlapping contours cross (the stem through a <c>$</c>), adjacent texels take their channels from
	/// different edges, and interpolating each channel separately then taking the median dips across 0.5 in a thin sliver
	/// well inside the ink. Checked on the 8-bit values the page will hold, as a renderer's filter reads them:
	/// <list type="bullet">
	/// <item>between neighbours along a row or column each channel is linear, so the median of the three is extreme only at
	/// the ends or where two channels cross; checking there, and where the true distance comes within
	/// <see cref="ClashMarginTexels"/> of the edge, finds every flip exactly;</item>
	/// <item>inside each square of four texels, on a grid of nine samples.</item>
	/// </list>
	/// A sample whose median is on the other side from the interpolated true distance, more than the margin from the edge,
	/// is settled by the exact fill: the true distance rounds sharp corners, which the channels exist to keep, but a median
	/// on the wrong side is a clash. The texels a clashing sample read collapse onto their true distance, which interpolates
	/// without clashing; only those lose their multi-channel sharpening. Later passes catch a clash an earlier one's
	/// flattening moved.
	/// </summary>
	private static void CorrectInterpolation(Shape shape, GlyphBitmap bitmap, CellProjection projection, double pxRangeTexels)
	{
		var w = bitmap.Width;
		var h = bitmap.Height;
		var px = bitmap.Pixels;
		var flatten = new bool[w * h];
		Span<double> candidates = stackalloc double[8];
		for (var pass = 0; pass < 3; pass++)
		{
			Array.Clear(flatten);
			var any = false;
			for (var y = 0; y < h; y++)
			{
				for (var x = 0; x < w; x++)
				{
					if (x + 1 < w)
						any |= CheckPair(x, y, x + 1, y, candidates);
					if (y + 1 < h)
						any |= CheckPair(x, y, x, y + 1, candidates);
					if (x + 1 >= w || y + 1 >= h)
						continue;
					for (var j = 1; j < 4; j++)
					{
						for (var i = 1; i < 4; i++)
							any |= CheckSquare(x, y, i / 4.0, j / 4.0);
					}
				}
			}

			if (!any)
				return;
			for (var i = 0; i < flatten.Length; i++)
			{
				if (!flatten[i])
					continue;
				var o = i * 4;
				px[o] = px[o + 1] = px[o + 2] = px[o + 3];
			}
		}

		// The texels (ax, ay) and (bx, by), one apart along a row or column.
		bool CheckPair(int ax, int ay, int bx, int by, Span<double> ts)
		{
			var a = (ay * w + ax) * 4;
			var b = (by * w + bx) * 4;
			var n = 0;
			for (var i = 0; i < 3; i++)
			{
				for (var j = i + 1; j < 3; j++)
				{
					var da = Quantize(px[a + i]) - Quantize(px[a + j]);
					var db = Quantize(px[b + i]) - Quantize(px[b + j]);
					if (da * db < 0)
						ts[n++] = da / (da - db);
				}
			}

			// Where the interpolated true distance crosses the margin either side of the edge.
			var ta = Quantize(px[a + 3]);
			var tb = Quantize(px[b + 3]);
			var margin = ClashMarginTexels / pxRangeTexels;
			foreach (var level in (ReadOnlySpan<double>)[0.5 - margin, 0.5 + margin])
			{
				if ((ta - level) * (tb - level) < 0)
					ts[n++] = (ta - level) / (ta - tb);
			}

			for (var k = 0; k < n; k++)
			{
				var t = ts[k];
				var median = Median(Lerp(a, b, 0, t), Lerp(a, b, 1, t), Lerp(a, b, 2, t));
				if (!Clashes(median, Lerp(a, b, 3, t), ax + (bx - ax) * t, ay + (by - ay) * t))
					continue;
				flatten[ay * w + ax] = flatten[by * w + bx] = true;
				return true;
			}

			return false;
		}

		// The sample at (x + ox, y + oy) inside the square of four texels from (x, y).
		bool CheckSquare(int x, int y, double ox, double oy)
		{
			Span<double> c = stackalloc double[4];
			var o00 = (y * w + x) * 4;
			var o10 = o00 + 4;
			var o01 = o00 + w * 4;
			var o11 = o01 + 4;
			for (var ch = 0; ch < 4; ch++)
			{
				var top = Quantize(px[o00 + ch]) * (1 - ox) + Quantize(px[o10 + ch]) * ox;
				var bottom = Quantize(px[o01 + ch]) * (1 - ox) + Quantize(px[o11 + ch]) * ox;
				c[ch] = top * (1 - oy) + bottom * oy;
			}

			if (!Clashes(Median(c[0], c[1], c[2]), c[3], x + ox, y + oy))
				return false;
			flatten[y * w + x] = flatten[y * w + x + 1] = flatten[(y + 1) * w + x] = flatten[(y + 1) * w + x + 1] = true;
			return true;
		}

		double Lerp(int a, int b, int channel, double t) => Quantize(px[a + channel]) * (1 - t) + Quantize(px[b + channel]) * t;

		// A median on the wrong side, away from the edge: the side the true distance gives, unless the exact fill there says
		// the true distance is the one that is wrong (rounding a sharp corner the median keeps).
		bool Clashes(double median, double trueValue, double sx, double sy)
		{
			if (Math.Abs(trueValue - 0.5) * pxRangeTexels < ClashMarginTexels || median > 0.5 == trueValue > 0.5)
				return false;
			return median > 0.5 != (shape.WindingAt(projection.Project(sx, sy)) != 0);
		}
	}

	/// <summary>How far from the true edge, in texels, a sample must be for its side to be checked against the exact fill:
	/// half a texel, inside which the antialiasing ramp legitimately straddles the edge.</summary>
	private const double ClashMarginTexels = 0.25;

	// A channel value as the page will hold it: the baker's quantization to a byte, back in 0..1.
	private static double Quantize(float v) => Math.Clamp((int)(v * 255f + 0.5f), 0, 255) / 255.0;

	private static float Median(float a, float b, float c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

	private static double Median(double a, double b, double c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
}
