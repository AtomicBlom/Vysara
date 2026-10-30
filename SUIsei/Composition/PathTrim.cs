namespace SUIsei.Composition;

/// <summary>
/// The part of a path's stroke that is drawn, as WinUI's <c>CompositionGeometry.TrimStart</c>, <c>TrimEnd</c> and
/// <c>TrimOffset</c>: fractions of the whole geometry's length, all its figures in order. The stroke runs from
/// <see cref="Start"/> to <see cref="End"/>, both moved by <see cref="Offset"/> and wrapping past the end of the path
/// back to its start, so animating <see cref="Offset"/> runs a fixed-length piece round a path. Start and end are
/// clamped to 0..1 and swapped when start is the larger; equal, they draw nothing.
/// <para>
/// A trimmed stroke takes its start cap where it starts and its end cap where it ends. Trimming changes which part of a
/// tessellated stroke is drawn, not the tessellation, so animating a trim never re-tessellates. It applies to strokes;
/// a fill is not trimmed.
/// </para>
/// </summary>
public readonly record struct PathTrim(float Start, float End, float Offset)
{
	/// <summary>The whole stroke: start 0, end 1, offset 0.</summary>
	public static PathTrim None => new(0f, 1f, 0f);

	/// <summary>Whether the whole stroke is drawn: start and end span the whole length, whatever the offset.</summary>
	public bool IsNone => MathF.Abs(Clamp(End) - Clamp(Start)) >= 1f;

	/// <summary>
	/// The visible part as up to two intervals of the length, as fractions in order along the path: 0 when nothing is
	/// drawn, 1, or 2 when the offset wraps the visible part past the path's end. <paramref name="first"/> starts at the
	/// lower fraction.
	/// </summary>
	public int Visible(out (float From, float To) first, out (float From, float To) second)
	{
		first = second = default;
		float s = Clamp(Start), e = Clamp(End);
		if (s > e)
			(s, e) = (e, s);
		if (e - s >= 1f)
		{
			first = (0f, 1f);
			return 1;
		}
		if (e <= s || !float.IsFinite(Offset))
			return 0;
		var o = Offset - MathF.Floor(Offset);
		float a = s + o, b = e + o;
		if (b <= 1f)
		{
			first = (a, b);
			return 1;
		}
		if (a >= 1f)
		{
			first = (a - 1f, b - 1f);
			return 1;
		}
		first = (0f, b - 1f);
		second = (a, 1f);
		return 2;
	}

	private static float Clamp(float v) => float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);
}
