namespace SUIsei.Media;

/// <summary>How a geometry whose figures overlap or cross themselves decides what is inside, as WinUI's
/// <c>FillRule</c>.</summary>
public enum FillRule
{
	/// <summary>A point is inside when a ray from it crosses the outline an odd number of times.</summary>
	EvenOdd,

	/// <summary>A point is inside when the outline winds around it a nonzero number of times, counting each crossing
	/// by its direction.</summary>
	Nonzero,
}
