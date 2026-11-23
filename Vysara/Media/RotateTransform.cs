using System.Numerics;

namespace Vysara.Media;

/// <summary>Rotates by <see cref="Angle"/> degrees, clockwise on screen, about (<see cref="CenterX"/>,
/// <see cref="CenterY"/>), as WinUI's <c>RotateTransform</c>.</summary>
public sealed class RotateTransform : Transform
{
	public static readonly DependencyProperty<RotateTransform, float> AngleProperty = Register<RotateTransform>(nameof(Angle));
	public static readonly DependencyProperty<RotateTransform, float> CenterXProperty = Register<RotateTransform>(nameof(CenterX));
	public static readonly DependencyProperty<RotateTransform, float> CenterYProperty = Register<RotateTransform>(nameof(CenterY));

	public float Angle { get => GetValue(AngleProperty); set => SetValue(AngleProperty, value); }

	public float CenterX { get => GetValue(CenterXProperty); set => SetValue(CenterXProperty, value); }

	public float CenterY { get => GetValue(CenterYProperty); set => SetValue(CenterYProperty, value); }

	internal override Matrix3x2 ToMatrix() => Matrix3x2.CreateRotation(Radians(Angle), new Vector2(CenterX, CenterY));
}
