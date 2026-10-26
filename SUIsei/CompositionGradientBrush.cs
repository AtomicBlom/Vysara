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
/// <see cref="SrgbSubBands"/> pieces sampled exactly at their ends.
/// </para>
/// </summary>
public abstract class CompositionGradientBrush : CompositionBrush
{
	/// <summary>
	/// How many pieces a stop interval is cut into when the brush interpolates in <see cref="CompositionColorSpace.Rgb"/>.
	/// Between the pieces' ends the colour is interpolated in linear light instead of sRGB. For opaque stops the error is
	/// at most (Δ²/512)·max|EOTF''| ≈ 0.0059·Δ² in linear light, where Δ is the change of a channel's sRGB value across the
	/// interval: under 0.006 for a full 0→1 step, which is less than one step of an 8-bit sRGB output.
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

	/// <summary>Throw when the stops cannot be interpolated in <see cref="InterpolationSpace"/>: sRGB interpolation is
	/// not defined above paper white, so an <see cref="CompositionColorSpace.Rgb"/> gradient with a stop above 1 is an
	/// error rather than a guess.</summary>
	internal void ThrowIfUndefined()
	{
		if (InterpolationSpace != CompositionColorSpace.Rgb)
			return;
		foreach (var stop in ColorStops)
		{
			var c = stop.Color;
			if (c.R > 1f || c.G > 1f || c.B > 1f)
				throw new InvalidOperationException(
					$"A gradient that interpolates in sRGB has a stop above paper white ({c.R:0.###}, {c.G:0.###}, {c.B:0.###}) at offset {stop.Offset:0.###}: " +
					"sRGB interpolation is not defined there. Interpolate in linear light (ScRgbLinearInterpolation) for colours above 1.");
		}
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
			return stops[0].Color.ToPremultipliedVector(opacity);
		var i = stops.Length - 1;
		while (stops[i].Offset > u)
			i--;
		if (i == stops.Length - 1)
			return stops[i].Color.ToPremultipliedVector(opacity);
		return Interpolate(stops[i], stops[i + 1], u, opacity);
	}

	// The colour at u approached from below: at a hard edge, the earlier stop's.
	private Vector4 SampleBelow(float u, float opacity)
	{
		var stops = ColorStops;
		if (stops.Length == 0)
			return Vector4.Zero;
		if (u > stops[^1].Offset)
			return stops[^1].Color.ToPremultipliedVector(opacity);
		var j = 0;
		while (stops[j].Offset < u)
			j++;
		if (j == 0)
			return stops[0].Color.ToPremultipliedVector(opacity);
		return Interpolate(stops[j - 1], stops[j], u, opacity);
	}

	// Between two stops with a.Offset <= u <= b.Offset and a.Offset < b.Offset. Written as a·(1−s) + b·s, which is exact
	// at both ends.
	private Vector4 Interpolate(in CompositionColorGradientStop a, in CompositionColorGradientStop b, float u, float opacity)
	{
		var s = (u - a.Offset) / (b.Offset - a.Offset);
		if (InterpolationSpace == CompositionColorSpace.RgbLinear)
			return a.Color.ToPremultipliedVector(opacity) * (1f - s) + b.Color.ToPremultipliedVector(opacity) * s;
		var ca = a.Color;
		var cb = b.Color;
		var r = 1f - s;
		return new HdrColor(ca.R * r + cb.R * s, ca.G * r + cb.G * s, ca.B * r + cb.B * s, ca.A * r + cb.A * s).ToPremultipliedVector(opacity);
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
