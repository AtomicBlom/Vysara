using System.Numerics;

namespace BinaryVibrance.Vysara.Media;

/// <summary>Skews about (<see cref="CenterX"/>, <see cref="CenterY"/>), as WinUI's <c>SkewTransform</c>: x moves by
/// y·tan(<see cref="AngleX"/>) and y by x·tan(<see cref="AngleY"/>), angles in degrees.</summary>
public sealed class SkewTransform : Transform
{
	public static readonly DependencyProperty<SkewTransform, float> AngleXProperty = Register<SkewTransform>(nameof(AngleX));
	public static readonly DependencyProperty<SkewTransform, float> AngleYProperty = Register<SkewTransform>(nameof(AngleY));
	public static readonly DependencyProperty<SkewTransform, float> CenterXProperty = Register<SkewTransform>(nameof(CenterX));
	public static readonly DependencyProperty<SkewTransform, float> CenterYProperty = Register<SkewTransform>(nameof(CenterY));

	public float AngleX { get => GetValue(AngleXProperty); set => SetValue(AngleXProperty, value); }

	public float AngleY { get => GetValue(AngleYProperty); set => SetValue(AngleYProperty, value); }

	public float CenterX { get => GetValue(CenterXProperty); set => SetValue(CenterXProperty, value); }

	public float CenterY { get => GetValue(CenterYProperty); set => SetValue(CenterYProperty, value); }

	internal override Matrix3x2 ToMatrix() =>
		Matrix3x2.CreateSkew(Radians(AngleX), Radians(AngleY), new Vector2(CenterX, CenterY));
}
