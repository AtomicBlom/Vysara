namespace SUIsei;

/// <summary>
/// The radii of a rectangle's four corners in DIPs, as WinUI's <c>CornerRadius</c>. SUIsei draws one radius for all
/// four corners, so an element given radii that differ throws <see cref="ArgumentException"/> where they are set rather
/// than drawing something else; radii bound to it from a template are not checked there, and draw at the top-left
/// corner's radius.
/// </summary>
public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
	/// <summary>The same radius at every corner.</summary>
	public CornerRadius(float uniformRadius) : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius) { }

	/// <summary>Whether all four corners share one radius.</summary>
	internal bool IsUniform => TopLeft == TopRight && TopLeft == BottomRight && TopLeft == BottomLeft;

	public override string ToString() => $"({TopLeft:0.###},{TopRight:0.###},{BottomRight:0.###},{BottomLeft:0.###})";
}
