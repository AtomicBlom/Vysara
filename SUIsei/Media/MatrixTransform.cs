using System.Numerics;

namespace SUIsei.Media;

/// <summary>
/// An arbitrary affine transform given as a matrix, as WinUI's <c>MatrixTransform</c>. SUIsei's matrix is
/// <see cref="Matrix3x2"/> (single precision, row vectors: M11 M12 / M21 M22 / M31 M32 as WinUI's M11 M12 / M21 M22 /
/// OffsetX OffsetY); in XAML it is six numbers in that order. Not animatable, as a matrix has no interpolation.
/// </summary>
public sealed class MatrixTransform : Transform
{
	public static readonly DependencyProperty<MatrixTransform, Matrix3x2> MatrixProperty =
		DependencyProperty.Register(nameof(Matrix),
			new PropertyMetadata<MatrixTransform, Matrix3x2> { DefaultValue = Matrix3x2.Identity, PropertyChanged = static (t, _, _) => t.Changed() });

	public Matrix3x2 Matrix { get => GetValue(MatrixProperty); set => SetValue(MatrixProperty, value); }

	internal override Matrix3x2 ToMatrix() => Matrix;
}
