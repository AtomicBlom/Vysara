using System.Numerics;

namespace BinaryVibrance.Vysara;

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

	/// <summary>The smallest rect holding this one and <paramref name="other"/>, where an empty one adds nothing.</summary>
	internal Rect Union(in Rect other)
	{
		if (IsEmpty)
			return other;
		if (other.IsEmpty)
			return this;
		var x = MathF.Min(X, other.X);
		var y = MathF.Min(Y, other.Y);
		return new Rect(x, y, MathF.Max(Right, other.Right) - x, MathF.Max(Bottom, other.Bottom) - y);
	}

	/// <summary>The axis-aligned bounds of this rect under an affine transform: exact for translate and scale, a
	/// conservative bound under rotation or skew. The compositor clips with it, and hit-testing uses the same bound so
	/// a point is hittable exactly where its subtree can draw.</summary>
	internal Rect TransformBounds(in Matrix3x2 m)
	{
		var a = Vector2.Transform(new Vector2(X, Y), m);
		var b = Vector2.Transform(new Vector2(Right, Y), m);
		var c = Vector2.Transform(new Vector2(Right, Bottom), m);
		var d = Vector2.Transform(new Vector2(X, Bottom), m);
		var minX = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
		var minY = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
		var maxX = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
		var maxY = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));
		return new Rect(minX, minY, maxX - minX, maxY - minY);
	}

	public override string ToString() => $"({X:0.###},{Y:0.###} {Width:0.###}x{Height:0.###})";
}
