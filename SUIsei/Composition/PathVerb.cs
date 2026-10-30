namespace SUIsei.Composition;

/// <summary>One drawing command of a <see cref="CompositionPath"/>, with the number of points it takes from
/// <see cref="CompositionPath.Points"/>.</summary>
public enum PathVerb : byte
{
	/// <summary>Start a figure at one point.</summary>
	Move,

	/// <summary>A straight line to one point.</summary>
	Line,

	/// <summary>A quadratic Bézier: a control point, then the end point.</summary>
	Quadratic,

	/// <summary>A cubic Bézier: two control points, then the end point.</summary>
	Cubic,

	/// <summary>Close the figure with a line back to its start; takes no points.</summary>
	Close,
}
