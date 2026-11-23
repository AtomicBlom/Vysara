using System.Globalization;

namespace Vysara.RoseTap;

/// <summary>
/// The rulers' arithmetic: how a hovered rectangle sits against the anchor's, axis by axis, and the words a pinned
/// measurement is sent in. Pure, so it is tested without an overlay.
/// <para>
/// An axis is a separation or a pair of edge distances and never both. Where the two spans miss each other the answer is
/// the gap; where they overlap it is how far each pair of edges is apart, which for a nested element is its padding or
/// margin, the number people come to the rulers for.
/// </para>
/// </summary>
internal static class TapMeasure
{
	/// <summary>One axis of a measurement.</summary>
	/// <param name="Separated">Whether the spans miss each other; touching counts, with a gap of zero.</param>
	/// <param name="Gap">The distance between the facing edges, where separated.</param>
	/// <param name="Before">Where separated, whether the hovered span lies before the anchor's (left of it, or above).</param>
	/// <param name="Low">Where overlapping, from the anchor's low edge in to the hovered's.</param>
	/// <param name="High">Where overlapping, from the anchor's high edge in to the hovered's.</param>
	public readonly record struct Axis(bool Separated, float Gap, bool Before, float Low, float High);

	/// <summary>The hovered span <c>[hoveredLow, hoveredHigh]</c> against the anchor's.</summary>
	public static Axis Measure(float anchorLow, float anchorHigh, float hoveredLow, float hoveredHigh)
	{
		if (hoveredLow >= anchorHigh)
			return new Axis(true, hoveredLow - anchorHigh, false, 0f, 0f);
		if (anchorLow >= hoveredHigh)
			return new Axis(true, anchorLow - hoveredHigh, true, 0f, 0f);
		return new Axis(false, 0f, false, hoveredLow - anchorLow, anchorHigh - hoveredHigh);
	}

	/// <summary>Where a line for one axis sits along the other: inside the band the two share, or midway between their
	/// facing edges where they share none.</summary>
	public static float Cross(float anchorLow, float anchorHigh, float hoveredLow, float hoveredHigh)
	{
		var sharedLow = MathF.Max(anchorLow, hoveredLow);
		var sharedHigh = MathF.Min(anchorHigh, hoveredHigh);
		if (sharedHigh >= sharedLow)
			return (sharedLow + sharedHigh) / 2f;
		return hoveredLow >= anchorHigh ? (anchorHigh + hoveredLow) / 2f : (hoveredHigh + anchorLow) / 2f;
	}

	/// <summary>The measurement from <paramref name="anchor"/> to <paramref name="other"/>, as an agent reads it.</summary>
	public static string Words(string anchor, string other, Axis horizontal, Axis vertical) =>
		$"the measurement from {anchor} to {other}: {AxisWords(horizontal, "horizontally", "left", "right")}; "
			+ AxisWords(vertical, "vertically", "top", "bottom");

	/// <summary>One axis as its numbers: the gap alone, or both edges' distances.</summary>
	public static string Numbers(Axis axis) => axis.Separated ? Number(axis.Gap) : $"{Number(axis.Low)} / {Number(axis.High)}";

	/// <summary>A length as a measurement says it: whole numbers bare, anything else to one decimal, since rounding
	/// 12.5 to 12 would be a confident wrong answer about the discrepancy somebody is hunting.</summary>
	public static string Number(float value)
	{
		var rounded = MathF.Round(value * 10f) / 10f;
		return rounded.ToString(rounded == MathF.Floor(rounded) ? "0" : "0.0", CultureInfo.InvariantCulture);
	}

	private static string AxisWords(Axis axis, string direction, string low, string high) => axis.Separated
		? $"{Number(axis.Gap)} px apart {direction}"
		: $"their {low} edges {Number(axis.Low)} px apart and their {high} edges {Number(axis.High)} px apart";
}
