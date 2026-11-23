using System.Numerics;

namespace BinaryVibrance.Vysara.Media;

/// <summary>Moves by (<see cref="X"/>, <see cref="Y"/>) DIPs, as WinUI's <c>TranslateTransform</c>.</summary>
public sealed class TranslateTransform : Transform
{
	public static readonly DependencyProperty<TranslateTransform, float> XProperty = Register<TranslateTransform>(nameof(X));
	public static readonly DependencyProperty<TranslateTransform, float> YProperty = Register<TranslateTransform>(nameof(Y));

	public float X { get => GetValue(XProperty); set => SetValue(XProperty, value); }

	public float Y { get => GetValue(YProperty); set => SetValue(YProperty, value); }

	internal override Matrix3x2 ToMatrix() => Matrix3x2.CreateTranslation(X, Y);
}
