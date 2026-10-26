using System.Numerics;

namespace SUIsei.Media;

/// <summary>
/// Scale, skew, rotation and translation in one, as WinUI's <c>CompositeTransform</c>, applied in that order: scale,
/// skew and rotation about (<see cref="CenterX"/>, <see cref="CenterY"/>), then the translation. Angles are in degrees.
/// The usual target of a storyboard that moves an element: <c>(UIElement.RenderTransform).(CompositeTransform.TranslateX)</c>.
/// </summary>
public sealed class CompositeTransform : Transform
{
	public static readonly DependencyProperty<CompositeTransform, float> CenterXProperty = Register<CompositeTransform>(nameof(CenterX));
	public static readonly DependencyProperty<CompositeTransform, float> CenterYProperty = Register<CompositeTransform>(nameof(CenterY));
	public static readonly DependencyProperty<CompositeTransform, float> ScaleXProperty = Register<CompositeTransform>(nameof(ScaleX), 1f);
	public static readonly DependencyProperty<CompositeTransform, float> ScaleYProperty = Register<CompositeTransform>(nameof(ScaleY), 1f);
	public static readonly DependencyProperty<CompositeTransform, float> SkewXProperty = Register<CompositeTransform>(nameof(SkewX));
	public static readonly DependencyProperty<CompositeTransform, float> SkewYProperty = Register<CompositeTransform>(nameof(SkewY));
	public static readonly DependencyProperty<CompositeTransform, float> RotationProperty = Register<CompositeTransform>(nameof(Rotation));
	public static readonly DependencyProperty<CompositeTransform, float> TranslateXProperty = Register<CompositeTransform>(nameof(TranslateX));
	public static readonly DependencyProperty<CompositeTransform, float> TranslateYProperty = Register<CompositeTransform>(nameof(TranslateY));

	public float CenterX { get => GetValue(CenterXProperty); set => SetValue(CenterXProperty, value); }

	public float CenterY { get => GetValue(CenterYProperty); set => SetValue(CenterYProperty, value); }

	public float ScaleX { get => GetValue(ScaleXProperty); set => SetValue(ScaleXProperty, value); }

	public float ScaleY { get => GetValue(ScaleYProperty); set => SetValue(ScaleYProperty, value); }

	public float SkewX { get => GetValue(SkewXProperty); set => SetValue(SkewXProperty, value); }

	public float SkewY { get => GetValue(SkewYProperty); set => SetValue(SkewYProperty, value); }

	public float Rotation { get => GetValue(RotationProperty); set => SetValue(RotationProperty, value); }

	public float TranslateX { get => GetValue(TranslateXProperty); set => SetValue(TranslateXProperty, value); }

	public float TranslateY { get => GetValue(TranslateYProperty); set => SetValue(TranslateYProperty, value); }

	internal override Matrix3x2 ToMatrix()
	{
		var center = new Vector2(CenterX, CenterY);
		return Matrix3x2.CreateTranslation(-center)
			* Matrix3x2.CreateScale(ScaleX, ScaleY)
			* Matrix3x2.CreateSkew(Radians(SkewX), Radians(SkewY))
			* Matrix3x2.CreateRotation(Radians(Rotation))
			* Matrix3x2.CreateTranslation(center.X + TranslateX, center.Y + TranslateY);
	}
}
