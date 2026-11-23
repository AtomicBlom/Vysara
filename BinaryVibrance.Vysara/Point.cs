using System.Numerics;

namespace BinaryVibrance.Vysara;

/// <summary>A point in DIPs (layout-space). Distinct from <see cref="Vector2"/> only for readability at call sites.</summary>
public readonly record struct Point(float X, float Y)
{
	public Vector2 ToVector2() => new(X, Y);
}
