using System.Numerics;
using BinaryVibrance.Vysara.Composition;

namespace BinaryVibrance.Vysara;

/// <summary>
/// Where a linear gradient lies in the local space of what it paints: <see cref="T"/> measures a point along it, 0 at
/// the start and 1 at the end, constant across it. A gradient whose start and end coincide is
/// <see cref="IsDegenerate"/>, and paints its last stop's colour everywhere.
/// </summary>
internal readonly struct GradientLine
{
	private readonly Vector2 _start;
	private readonly Vector2 _direction;
	private readonly float _lengthSquared;

	public GradientLine(Vector2 start, Vector2 end)
	{
		_start = start;
		_direction = end - start;
		_lengthSquared = _direction.LengthSquared();
		IsDegenerate = !(_lengthSquared > 1e-12f) || !float.IsFinite(_lengthSquared);
	}

	/// <summary>The line of <paramref name="brush"/> over a box of <paramref name="size"/> at the origin.</summary>
	public static GradientLine For(CompositionLinearGradientBrush brush, Vector2 size)
	{
		brush.Map(size, out var start, out var end);
		return new GradientLine(start, end);
	}

	/// <summary>The line of <paramref name="brush"/> over <paramref name="box"/>: relative points measure across the box
	/// from its corner, absolute ones are in the box's own space, as for a shape whose geometry does not start at the
	/// origin.</summary>
	public static GradientLine For(CompositionLinearGradientBrush brush, in Rect box)
	{
		brush.Map(new Vector2(box.Width, box.Height), out var start, out var end);
		if (brush.MappingMode == CompositionMappingMode.Relative)
		{
			var corner = new Vector2(box.X, box.Y);
			start += corner;
			end += corner;
		}
		return new GradientLine(start, end);
	}

	/// <summary>Whether the start and end coincide, so the gradient has no direction.</summary>
	public bool IsDegenerate { get; }

	/// <summary>This line moved by <paramref name="by"/>, for paint that moves with what it paints.</summary>
	public GradientLine Offset(Vector2 by) => by == Vector2.Zero ? this : new GradientLine(_start + by, _start + by + _direction);

	/// <summary>How far along the gradient <paramref name="point"/> lies: 0 at the start, 1 at the end. The division
	/// comes last, so the end itself measures exactly 1 and a point at a stop's offset exactly that offset whenever the
	/// numbers allow.</summary>
	public float T(Vector2 point) => Vector2.Dot(point - _start, _direction) / _lengthSquared;
}
