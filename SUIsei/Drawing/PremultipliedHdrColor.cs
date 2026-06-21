using System.Numerics;
using System.Runtime.InteropServices;

namespace SUIsei.Drawing;

/// <summary>
/// A premultiplied RGBA16F colour — the wire format for the overlay vertex stream. Four Half components (8 bytes),
/// blittable and sequentially laid out so the engine can upload it directly as GPU vertex data. Named
/// PremultipliedHdrColor (not a bare four-Half vector) so the premultiplied-alpha contract is explicit in the type system;
/// produced by <see cref="HdrColor.ToPremultiplied"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PremultipliedHdrColor(Half R, Half G, Half B, Half A)
{
	/// <summary>Fully transparent (all components zero) — the premultiplied "nothing".</summary>
	public static PremultipliedHdrColor Zero => new((Half)0f, (Half)0f, (Half)0f, (Half)0f);

	/// <summary>Widen the four Half channels to a single-precision RGBA vector (R→X … A→W).</summary>
	public Vector4 ToVector4() => new((float)R, (float)G, (float)B, (float)A);

	/// <summary>
	/// Build a premultiplied colour from straight (non-premultiplied) float RGBA — the common authoring path:
	/// RGB is scaled by <paramref name="a"/> so alpha-over compositing is correct.
	/// </summary>
	public static PremultipliedHdrColor FromStraight(float r, float g, float b, float a) =>
		new((Half)(r * a), (Half)(g * a), (Half)(b * a), (Half)a);

	/// <summary>Build from components that are already premultiplied (e.g. a folded group opacity).</summary>
	public static PremultipliedHdrColor FromPremultiplied(float r, float g, float b, float a) =>
		new((Half)r, (Half)g, (Half)b, (Half)a);
}
