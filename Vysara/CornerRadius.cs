using System.Globalization;

namespace Vysara;

/// <summary>
/// The radii of a rectangle's four corners in DIPs, as WinUI's <c>CornerRadius</c>, clockwise from the top left. Each
/// corner is a circular arc of its own radius.
/// </summary>
public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
	/// <summary>The same radius at every corner.</summary>
	public CornerRadius(float uniformRadius) : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius) { }

	public override string ToString() =>
		string.Create(CultureInfo.InvariantCulture, $"({TopLeft:0.###},{TopRight:0.###},{BottomRight:0.###},{BottomLeft:0.###})");
}
