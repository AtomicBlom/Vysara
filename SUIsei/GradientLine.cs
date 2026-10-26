using System.Numerics;

namespace SUIsei;

/// <summary>
/// Where a linear gradient lies in the local space of what it paints: <see cref="T"/> measures a point along it, 0 at
/// the start and 1 at the end, constant across it. A gradient whose start and end coincide is
/// <see cref="IsDegenerate"/>, and paints its last stop's colour everywhere.
/// </summary>
internal readonly struct GradientLine
{
	private readonly Vector2 _start;
	private readonly Vector2 _step; // the direction divided by its squared length, so T is one dot product

	public GradientLine(Vector2 start, Vector2 end)
	{
		var direction = end - start;
		var lengthSquared = direction.LengthSquared();
		IsDegenerate = !(lengthSquared > 1e-12f) || !float.IsFinite(lengthSquared);
		_start = start;
		_step = IsDegenerate ? Vector2.Zero : direction / lengthSquared;
	}

	/// <summary>The line of <paramref name="brush"/> over a box of <paramref name="size"/> at the origin.</summary>
	public static GradientLine For(CompositionLinearGradientBrush brush, Vector2 size)
	{
		brush.Map(size, out var start, out var end);
		return new GradientLine(start, end);
	}

	/// <summary>Whether the start and end coincide, so the gradient has no direction.</summary>
	public bool IsDegenerate { get; }

	/// <summary>How far along the gradient <paramref name="point"/> lies: 0 at the start, 1 at the end.</summary>
	public float T(Vector2 point) => Vector2.Dot(point - _start, _step);
}
