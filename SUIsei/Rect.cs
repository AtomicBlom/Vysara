using System.Numerics;

namespace SUIsei;

/// <summary>
/// An axis-aligned rectangle in pixels — neutral geometry used for layout, hit-testing, clip rects, and draw commands.
/// Stored as origin + size so a zero-size rect is unambiguous; Right/Bottom are derived.
/// </summary>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
	/// <summary>An empty rect at the origin.</summary>
	public static Rect Zero => new(0f, 0f, 0f, 0f);

	/// <summary>Right edge (X + Width).</summary>
	public float Right => X + Width;

	/// <summary>Bottom edge (Y + Height).</summary>
	public float Bottom => Y + Height;

	/// <summary>True when this rect encloses zero area.</summary>
	public bool IsEmpty => Width <= 0f || Height <= 0f;

	/// <summary>True if <paramref name="p"/> lies within this rect (left/top inclusive, right/bottom exclusive).</summary>
	public bool Contains(Vector2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

	/// <summary>The overlap of this rect with <paramref name="other"/>; IsEmpty when they do not overlap.</summary>
	public Rect Intersect(in Rect other)
	{
		var x = MathF.Max(X, other.X);
		var y = MathF.Max(Y, other.Y);
		var right = MathF.Min(Right, other.Right);
		var bottom = MathF.Min(Bottom, other.Bottom);
		return right <= x || bottom <= y ? Zero : new Rect(x, y, right - x, bottom - y);
	}

	public override string ToString() => $"({X:0.###},{Y:0.###} {Width:0.###}x{Height:0.###})";
}
