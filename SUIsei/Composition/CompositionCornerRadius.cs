using System.Numerics;

namespace SUIsei.Composition;

/// <summary>
/// The radii of a rectangle's four circular corners in its local units, clockwise from the top left as WinUI's
/// <c>CornerRadius</c> orders them. A radius of 0 or less draws a square corner.
/// </summary>
public readonly record struct CompositionCornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
	/// <summary>The same radius at every corner.</summary>
	public CompositionCornerRadius(float uniformRadius) : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius) { }

	/// <summary>The same radius at every corner.</summary>
	public static implicit operator CompositionCornerRadius(float uniformRadius) => new(uniformRadius);

	/// <summary>Whether all four corners share one radius, which the rounded-rect prims draw.</summary>
	public bool IsUniform => TopLeft == TopRight && TopLeft == BottomRight && TopLeft == BottomLeft;

	/// <summary>Whether any corner is rounded.</summary>
	public bool IsRounded => TopLeft > 0f || TopRight > 0f || BottomRight > 0f || BottomLeft > 0f;

	/// <summary>
	/// The radii a rectangle of <paramref name="size"/> draws: each at least 0, then all four scaled down by one factor
	/// until the two corners on every side fit along it, as CSS fits a box's corners. A uniform radius comes out at most
	/// half the shorter side, as the rounded-rect prims draw it.
	/// </summary>
	public CompositionCornerRadius FitTo(Vector2 size)
	{
		float tl = Positive(TopLeft), tr = Positive(TopRight), br = Positive(BottomRight), bl = Positive(BottomLeft);
		var scale = 1f;
		Fit(tl + tr, size.X);
		Fit(bl + br, size.X);
		Fit(tl + bl, size.Y);
		Fit(tr + br, size.Y);
		return scale == 1f ? new(tl, tr, br, bl) : new(tl * scale, tr * scale, br * scale, bl * scale);

		void Fit(float corners, float side)
		{
			if (corners > side)
				scale = MathF.Min(scale, MathF.Max(0f, side) / corners);
		}

		// NaN, like a negative radius, draws a square corner.
		static float Positive(float radius) => radius > 0f ? radius : 0f;
	}

	public override string ToString() => $"({TopLeft:0.###},{TopRight:0.###},{BottomRight:0.###},{BottomLeft:0.###})";
}
