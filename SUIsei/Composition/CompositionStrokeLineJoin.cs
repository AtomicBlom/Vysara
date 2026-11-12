namespace SUIsei.Composition;

/// <summary>How a stroke turns a corner, as WinUI's <c>CompositionStrokeLineJoin</c>.</summary>
public enum CompositionStrokeLineJoin
{
	/// <summary>The outer edges are extended until they meet, cut off at the stroke style's miter limit.</summary>
	Miter,

	/// <summary>The outer corners are joined by a straight edge.</summary>
	Bevel,

	/// <summary>The outer corners are joined by an arc centred on the corner.</summary>
	Round,
}
