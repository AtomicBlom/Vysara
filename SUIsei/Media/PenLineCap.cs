namespace SUIsei.Media;

/// <summary>The shape at the end of a stroke or of one of its dashes, as WinUI's <c>PenLineCap</c>.</summary>
public enum PenLineCap
{
	/// <summary>The stroke ends square at its end point.</summary>
	Flat,

	/// <summary>The stroke ends square, half its thickness past its end point.</summary>
	Square,

	/// <summary>A half circle, with a radius of half the stroke's thickness, past the end point.</summary>
	Round,

	/// <summary>A triangle whose apex is half the stroke's thickness past the end point.</summary>
	Triangle,
}
