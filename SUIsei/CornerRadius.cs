namespace SUIsei;

/// <summary>
/// The radii of a rectangle's four corners in DIPs, as WinUI's <c>CornerRadius</c>. SUIsei draws one radius for all
/// four corners, so an element given radii that differ throws <see cref="NotSupportedException"/> rather than drawing
/// something else.
/// </summary>
public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
	/// <summary>The same radius at every corner.</summary>
	public CornerRadius(float uniformRadius) : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius) { }

	/// <summary>Whether all four corners share one radius.</summary>
	internal bool IsUniform => TopLeft == TopRight && TopLeft == BottomRight && TopLeft == BottomLeft;

	/// <summary>The one radius a uniform value holds; throws for radii that differ, which SUIsei cannot draw yet.</summary>
	internal float Uniform => IsUniform
		? TopLeft
		: throw new NotSupportedException($"Corner radii that differ ({this}) are not supported yet: SUIsei draws one radius for all four corners.");

	public override string ToString() => $"({TopLeft:0.###},{TopRight:0.###},{BottomRight:0.###},{BottomLeft:0.###})";
}
