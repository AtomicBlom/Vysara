namespace BinaryVibrance.Vysara.Composition;

/// <summary>The shape at the end of an open figure or a dash, as WinUI's <c>CompositionStrokeCap</c>.</summary>
public enum CompositionStrokeCap
{
	/// <summary>The stroke ends square at the end point.</summary>
	Flat,

	/// <summary>The stroke ends square, half a thickness past the end point.</summary>
	Square,

	/// <summary>A half circle, a thickness across, centred on the end point.</summary>
	Round,

	/// <summary>A triangle reaching half a thickness past the end point.</summary>
	Triangle,
}
