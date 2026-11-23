using System.Numerics;

namespace BinaryVibrance.Vysara.Animation;

/// <summary>
/// The standard linear interpolations. A dependency property of one of these types is animatable without declaring
/// a <see cref="PropertyMetadata{TOwner, TValue}.Lerp"/>, and <see cref="AnimationClock"/> tweens them without one.
/// </summary>
public static class Lerps
{
	public static Lerp<float> Float { get; } = static (a, b, t) => a + (b - a) * t;

	public static Lerp<double> Double { get; } = static (a, b, t) => a + (b - a) * t;

	public static Lerp<Vector2> Vector2 { get; } = System.Numerics.Vector2.Lerp;

	public static Lerp<Vector3> Vector3 { get; } = System.Numerics.Vector3.Lerp;

	public static Lerp<Point> Point { get; } = static (a, b, t) => new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

	/// <summary>Component-wise in the authored colour space, alpha included.</summary>
	public static Lerp<HdrColor> Color { get; } = static (a, b, t) =>
		new HdrColor(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

	/// <summary>The standard interpolation for <typeparamref name="T"/>, or null when it has none.</summary>
	public static Lerp<T>? For<T>() => Standard<T>.Value;

	// One lookup per closed type, resolved once. The typeof comparisons fold away when T is known, so this stays
	// trimming- and NativeAOT-safe.
	private static class Standard<T>
	{
		public static readonly Lerp<T>? Value =
			typeof(T) == typeof(float) ? (Lerp<T>)(object)Float
			: typeof(T) == typeof(double) ? (Lerp<T>)(object)Double
			: typeof(T) == typeof(Vector2) ? (Lerp<T>)(object)Vector2
			: typeof(T) == typeof(Vector3) ? (Lerp<T>)(object)Vector3
			: typeof(T) == typeof(Point) ? (Lerp<T>)(object)Point
			: typeof(T) == typeof(HdrColor) ? (Lerp<T>)(object)Color
			: null;
	}
}
