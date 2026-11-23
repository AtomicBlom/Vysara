namespace BinaryVibrance.Vysara.Media;

/// <summary>What a gradient paints before its start and past its end, as WinUI's <c>GradientSpreadMethod</c>.</summary>
public enum GradientSpreadMethod
{
	/// <summary>The end colours continue outward.</summary>
	Pad,

	/// <summary>The gradient repeats, every other repeat running backwards.</summary>
	Reflect,

	/// <summary>The gradient repeats from its first stop.</summary>
	Repeat,
}
