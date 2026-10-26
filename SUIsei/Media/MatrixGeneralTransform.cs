using System.Numerics;

namespace SUIsei.Media;

/// <summary>An affine <see cref="GeneralTransform"/>: what <see cref="UIElement.TransformToVisual"/> returns for the
/// composed visual transforms between two elements.</summary>
internal sealed class MatrixGeneralTransform(Matrix3x2 matrix) : GeneralTransform
{
	internal Matrix3x2 Matrix { get; } = matrix;

	protected override GeneralTransform? InverseCore =>
		Matrix3x2.Invert(Matrix, out var inverse) ? new MatrixGeneralTransform(inverse) : null;

	protected override bool TryTransformCore(Point inPoint, out Point outPoint)
	{
		var p = Vector2.Transform(inPoint.ToVector2(), Matrix);
		outPoint = new Point(p.X, p.Y);
		return float.IsFinite(p.X) && float.IsFinite(p.Y);
	}

	protected override Rect TransformBoundsCore(Rect rect) => rect.TransformBounds(Matrix);
}
