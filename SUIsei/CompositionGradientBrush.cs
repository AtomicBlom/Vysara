using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei;

/// <summary>
/// The colour stops and modes a gradient shares, as WinUI's <c>CompositionGradientBrush</c>; a
/// <see cref="CompositionLinearGradientBrush"/> places them along a line. Stops are kept ordered by offset, and two stops
/// at one offset make a hard edge between their colours.
/// <para>
/// Colours interpolate as premultiplied values, the way a GPU interpolates vertex colours. In
/// <see cref="CompositionColorSpace.RgbLinear"/> that is linear light, which a compositor reproduces exactly by splitting
/// the painted geometry where the stops fall and colouring the pieces' corners, with no ramp texture to clamp or quantize
/// colours above paper white. In <see cref="CompositionColorSpace.Rgb"/> the interpolation is in the authored,
/// sRGB-encoded values, which are not linear in the light the GPU blends, so each stop interval is cut into
/// <see cref="SrgbSubBands"/> pieces sampled exactly at their ends. Between those ends the colour is the straight line in
/// linear light wherever it is sampled, so polygons that share a point, such as the triangles of a shape, agree on its
/// colour. sRGB interpolation is not defined above paper white, so there a stop's channels above 1 are drawn at 1, as
/// they would be by any 8-bit colour; a colour animation that overshoots, or a stop set above 1, never stops the frame.
/// Interpolate in linear light to keep colours above 1.
/// </para>
/// </summary>
public abstract class CompositionGradientBrush : CompositionBrush
{
	/// <summary>
	/// How many pieces a stop interval is cut into when the brush interpolates in <see cref="CompositionColorSpace.Rgb"/>.
	/// The pieces' ends are exact; between them the colour is interpolated in linear light instead of in sRGB. For opaque
	/// stops the error is at most (Δ²/512)·max|EOTF''| ≈ 0.0059·Δ² in linear light, where Δ is the change of a channel's
	/// sRGB value across the interval, so under 0.006 even for a full 0→1 step. In an 8-bit sRGB output that is under one
	/// step from mid-grey up, and up to five steps near black, where the sRGB encoding is steepest; both shrink with the
	/// square of the step between stops.
	/// </summary>
	public const int SrgbSubBands = 8;

	// The stops, ordered by offset, and a buffer the next set is ordered in before it is compared. Both only grow.
	private CompositionColorGradientStop[] _stops = [];
	private CompositionColorGradientStop[] _scratch = [];
	private int _count;

	private protected CompositionGradientBrush()
	{
	}

	/// <summary>The stops, ordered by offset.</summary>
	public ReadOnlySpan<CompositionColorGradientStop> ColorStops => _stops.AsSpan(0, _count);

	/// <summary>What the brush's points are measured in. Defaults to fractions of the box painted.</summary>
	public CompositionMappingMode MappingMode { get; set => Change(ref field, value); } = CompositionMappingMode.Relative;

	/// <summary>What the brush paints before its first stop and past its last.</summary>
	public CompositionGradientExtendMode ExtendMode { get; set => Change(ref field, value); } = CompositionGradientExtendMode.Clamp;

	/// <summary>The space colours interpolate in. Defaults to the authored sRGB values, as XAML gradients do.</summary>
	public CompositionColorSpace InterpolationSpace { get; set => Change(ref field, value); } = CompositionColorSpace.Rgb;

	/// <summary>
	/// Replace the stops, ordering them by offset; stops at one offset keep the order given. Advances
	/// <see cref="CompositionBrush.Version"/> only if the ordered stops differ, and allocates only when there are more stops
	/// than the brush has held before.
	/// </summary>
	public void SetColorStops(ReadOnlySpan<CompositionColorGradientStop> stops)
	{
		if (_scratch.Length < stops.Length)
			_scratch = new CompositionColorGradientStop[stops.Length];
		var ordered = _scratch.AsSpan(0, stops.Length);
		stops.CopyTo(ordered);
		// Insertion sort: stable, allocation-free, and stops come few and usually ordered.
		for (var i = 1; i < ordered.Length; i++)
		{
			var stop = ordered[i];
			var j = i - 1;
			for (; j >= 0 && ordered[j].Offset > stop.Offset; j--)
				ordered[j + 1] = ordered[j];
			ordered[j + 1] = stop;
		}
		if (ordered.SequenceEqual(ColorStops))
			return;
		(_stops, _scratch) = (_scratch, _stops);
		_count = stops.Length;
		Touch();
	}

	// --- Evaluation along the gradient: t is 0 at its start and 1 at its end; u is t brought into the stops' range by
	// the extend mode. Colours are premultiplied linear light with `opacity` folded in, computed as
	// HdrColor.ToPremultiplied computes them, so a sample at a stop is that stop's colour exactly. ---

	/// <summary>The colour approaching <paramref name="t"/> from below: where the gradient jumps (a hard edge, a wrap),
	/// the colour before the jump.</summary>
	internal Vector4 EvaluateBelow(float t, float opacity)
	{
		if (ExtendMode == CompositionGradientExtendMode.Clamp)
			return SampleBelow(t, opacity);
		var k = MathF.Ceiling(t) - 1f;
		var f = t - k;
		return ExtendMode == CompositionGradientExtendMode.Mirror && IsOdd(k) ? SampleAbove(1f - f, opacity) : SampleBelow(f, opacity);
	}

	/// <summary>The colour at <paramref name="t"/> and just past it: where the gradient jumps, the colour after the
	/// jump.</summary>
	internal Vector4 EvaluateAbove(float t, float opacity)
	{
		if (ExtendMode == CompositionGradientExtendMode.Clamp)
			return SampleAbove(t, opacity);
		var k = MathF.Floor(t);
		var f = t - k;
		return ExtendMode == CompositionGradientExtendMode.Mirror && IsOdd(k) ? SampleBelow(1f - f, opacity) : SampleAbove(f, opacity);
	}

	/// <summary>The first place after <paramref name="t"/> where the colour stops being linear in t: a stop, a wrap or
	/// mirror boundary, or the end of an sRGB piece. +∞ when the colour is linear from here on.</summary>
	internal float NextBreak(float t)
	{
		if (ExtendMode == CompositionGradientExtendMode.Clamp)
			return NextBreakAfter(t);
		var k = MathF.Floor(t);
		var f = t - k;
		if (ExtendMode == CompositionGradientExtendMode.Mirror && IsOdd(k))
		{
			// Running backwards through the stops: the next break in t is the previous one in u.
			var b = PreviousBreakBefore(1f - f);
			return b > 0f ? k + (1f - b) : k + 1f;
		}
		var next = NextBreakAfter(f);
		return next < 1f ? k + next : k + 1f;
	}

	private static bool IsOdd(float k) => ((long)k & 1) != 0;

	// The smallest stop offset or sRGB piece end greater than u, else +∞.
	private float NextBreakAfter(float u)
	{
		var stops = ColorStops;
		var pieces = InterpolationSpace == CompositionColorSpace.Rgb;
		for (var i = 0; i < stops.Length; i++)
		{
			var o = stops[i].Offset;
			if (o > u)
				return o;
			if (pieces && i + 1 < stops.Length && stops[i + 1].Offset > u)
			{
				var span = stops[i + 1].Offset - o;
				for (var p = 1; p < SrgbSubBands; p++)
				{
					var point = o + span * p / SrgbSubBands;
					if (point > u)
						return point;
				}
			}
		}
		return float.PositiveInfinity;
	}

	// The largest stop offset or sRGB piece end less than u, else -∞.
	private float PreviousBreakBefore(float u)
	{
		var stops = ColorStops;
		var pieces = InterpolationSpace == CompositionColorSpace.Rgb;
		for (var i = stops.Length - 1; i >= 0; i--)
		{
			var o = stops[i].Offset;
			if (o < u)
			{
				if (pieces && i + 1 < stops.Length)
				{
					var span = stops[i + 1].Offset - o;
					for (var p = SrgbSubBands - 1; p >= 1; p--)
					{
						var point = o + span * p / SrgbSubBands;
						if (point < u)
							return point;
					}
				}
				return o;
			}
		}
		return float.NegativeInfinity;
	}

	// The colour at u approached from above: at a hard edge, the later stop's.
	private Vector4 SampleAbove(float u, float opacity)
	{
		var stops = ColorStops;
		if (stops.Length == 0)
			return Vector4.Zero;
		if (u < stops[0].Offset)
			return StopColor(stops[0], opacity);
		var i = stops.Length - 1;
		while (stops[i].Offset > u)
			i--;
		if (i == stops.Length - 1)
			return StopColor(stops[i], opacity);
		return Interpolate(stops[i], stops[i + 1], u, opacity);
	}

	// The colour at u approached from below: at a hard edge, the earlier stop's.
	private Vector4 SampleBelow(float u, float opacity)
	{
		var stops = ColorStops;
		if (stops.Length == 0)
			return Vector4.Zero;
		if (u > stops[^1].Offset)
			return StopColor(stops[^1], opacity);
		var j = 0;
		while (stops[j].Offset < u)
			j++;
		if (j == 0)
			return StopColor(stops[0], opacity);
		return Interpolate(stops[j - 1], stops[j], u, opacity);
	}

	/// <summary>The colour of <paramref name="stop"/> where the gradient holds it unchanged (before the first stop, past the
	/// last, or throughout one with no direction to vary along): in sRGB clamped to paper white as an interpolated colour is,
	/// so the colour runs on into the stops without a step.</summary>
	internal Vector4 StopColor(in CompositionColorGradientStop stop, float opacity)
	{
		var c = stop.Color;
		if (InterpolationSpace == CompositionColorSpace.Rgb)
			c = new HdrColor(MathF.Min(c.R, 1f), MathF.Min(c.G, 1f), MathF.Min(c.B, 1f), c.A);
		return c.ToPremultipliedVector(opacity);
	}

	// Between two stops with a.Offset <= u <= b.Offset and a.Offset < b.Offset. Written as a·(1−s) + b·s, which is exact
	// at both ends.
	private Vector4 Interpolate(in CompositionColorGradientStop a, in CompositionColorGradientStop b, float u, float opacity)
	{
		var span = b.Offset - a.Offset;
		var s = (u - a.Offset) / span;
		if (InterpolationSpace == CompositionColorSpace.RgbLinear)
			return a.Color.ToPremultipliedVector(opacity) * (1f - s) + b.Color.ToPremultipliedVector(opacity) * s;

		// In sRGB the colour drawn is the chord across the piece u lies in, between exact samples at the piece's ends (the
		// points NextBreakAfter cuts at, computed the same way). Every polygon that meets u then colours it the same, wherever
		// its own bands start: two triangles sharing a corner inside a piece agree on it.
		var k = Math.Clamp((int)MathF.Floor(s * SrgbSubBands), 0, SrgbSubBands - 1);
		var p0 = k == 0 ? a.Offset : a.Offset + span * k / SrgbSubBands;
		var p1 = k + 1 == SrgbSubBands ? b.Offset : a.Offset + span * (k + 1) / SrgbSubBands;
		var f = Math.Clamp((u - p0) / (p1 - p0), 0f, 1f);
		return Srgb(a.Color, b.Color, (p0 - a.Offset) / span, opacity) * (1f - f) + Srgb(a.Color, b.Color, (p1 - a.Offset) / span, opacity) * f;

		// The exact sRGB interpolation at fraction t of the interval, of the stops clamped to paper white: sRGB
		// interpolation is not defined above it.
		static Vector4 Srgb(HdrColor ca, HdrColor cb, float t, float opacity)
		{
			var r = 1f - t;
			return new HdrColor(MathF.Min(ca.R, 1f) * r + MathF.Min(cb.R, 1f) * t, MathF.Min(ca.G, 1f) * r + MathF.Min(cb.G, 1f) * t,
				MathF.Min(ca.B, 1f) * r + MathF.Min(cb.B, 1f) * t, ca.A * r + cb.A * t).ToPremultipliedVector(opacity);
		}
	}

	/// <summary>Append the modes and stops to a dump line.</summary>
	private protected void DescribeStops(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(MappingMode == CompositionMappingMode.Relative ? " rel" : " abs");
		if (ExtendMode != CompositionGradientExtendMode.Clamp)
			sb.Append(ExtendMode == CompositionGradientExtendMode.Wrap ? " wrap" : " mirror");
		sb.Append(InterpolationSpace == CompositionColorSpace.Rgb ? " srgb" : " linear");
		foreach (var stop in ColorStops)
			sb.Append(c, $" {stop.Offset:0.###}:({stop.Color.R:0.###},{stop.Color.G:0.###},{stop.Color.B:0.###},{stop.Color.A:0.###})");
		if (Opacity != 1f)
			sb.Append(c, $" *{Opacity:0.###}");
	}
}
