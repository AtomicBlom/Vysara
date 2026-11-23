namespace Vysara.Composition;

/// <summary>How a path's overlapping figures and self-crossings decide what its fill covers.</summary>
public enum CompositionFillRule
{
	/// <summary>A point is inside where a ray from it crosses the outline an odd number of times.</summary>
	EvenOdd,

	/// <summary>A point is inside where the outline winds round it a number of times other than zero.</summary>
	Nonzero,
}
