namespace SUIsei.Media;

/// <summary>How a stroke turns a corner, as WinUI's <c>PenLineJoin</c>.</summary>
public enum PenLineJoin
{
	/// <summary>The outer edges run on until they meet, cut off at the miter limit.</summary>
	Miter,

	/// <summary>The outer corner is cut straight across.</summary>
	Bevel,

	/// <summary>The outer corner is rounded, with a radius of half the stroke's thickness.</summary>
	Round,
}
