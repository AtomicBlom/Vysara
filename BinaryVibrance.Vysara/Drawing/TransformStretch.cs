using System.Numerics;

namespace BinaryVibrance.Vysara.Drawing;

/// <summary>
/// How much a 2D transform stretches lengths: the largest and smallest factors over all directions, the singular values
/// of its linear part. A uniform scale <c>s</c> stretches every direction by <c>s</c> and a rotation by 1; a non-uniform
/// scale or a skew stretches some directions more than others. Translation stretches nothing.
/// </summary>
internal readonly record struct TransformStretch(float Largest, float Smallest)
{
	public static TransformStretch Of(in Matrix3x2 transform)
	{
		double a = transform.M11, b = transform.M12, c = transform.M21, d = transform.M22;
		var e = (a * a + b * b + c * c + d * d) / 2;
		var det = a * d - b * c;
		var spread = Math.Sqrt(Math.Max(0, e * e - det * det));
		return new TransformStretch((float)Math.Sqrt(e + spread), (float)Math.Sqrt(Math.Max(0, e - spread)));
	}
}
