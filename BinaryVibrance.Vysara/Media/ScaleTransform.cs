using System.Numerics;

namespace BinaryVibrance.Vysara.Media;

/// <summary>Scales by <see cref="ScaleX"/> and <see cref="ScaleY"/> about (<see cref="CenterX"/>, <see cref="CenterY"/>),
/// as WinUI's <c>ScaleTransform</c>.</summary>
public sealed class ScaleTransform : Transform
{
	public static readonly DependencyProperty<ScaleTransform, float> ScaleXProperty = Register<ScaleTransform>(nameof(ScaleX), 1f);
	public static readonly DependencyProperty<ScaleTransform, float> ScaleYProperty = Register<ScaleTransform>(nameof(ScaleY), 1f);
	public static readonly DependencyProperty<ScaleTransform, float> CenterXProperty = Register<ScaleTransform>(nameof(CenterX));
	public static readonly DependencyProperty<ScaleTransform, float> CenterYProperty = Register<ScaleTransform>(nameof(CenterY));

	public float ScaleX { get => GetValue(ScaleXProperty); set => SetValue(ScaleXProperty, value); }

	public float ScaleY { get => GetValue(ScaleYProperty); set => SetValue(ScaleYProperty, value); }

	public float CenterX { get => GetValue(CenterXProperty); set => SetValue(CenterXProperty, value); }

	public float CenterY { get => GetValue(CenterYProperty); set => SetValue(CenterYProperty, value); }

	internal override Matrix3x2 ToMatrix() => Matrix3x2.CreateScale(ScaleX, ScaleY, new Vector2(CenterX, CenterY));
}
