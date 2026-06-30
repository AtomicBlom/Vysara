using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// An authoring colour — float RGBA where <c>0..1</c> is SDR / paper-white range and a component <c>&gt;1</c> is
/// above paper white (opt-in HDR). Named <c>HdrColor</c> (not <c>Color</c>) for explicitness and to dodge the
/// <see cref="System.Drawing.Color"/> clash. Folded to a
/// <see cref="PremultipliedHdrColor"/> by the compositor; the paper-white reference scale is applied in the shader.
/// </summary>
public readonly record struct HdrColor(float R, float G, float B, float A)
{
	public static HdrColor Transparent => new(0f, 0f, 0f, 0f);

	/// <summary>From 8-bit sRGB-ish channels (0..255) at full alpha — the common HUD authoring shorthand.</summary>
	public static HdrColor FromRgb(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

	/// <summary>
	/// Fold in a group/animation opacity and convert to a premultiplied <see cref="PremultipliedHdrColor"/> for the
	/// draw stream. The authored RGB is sRGB, so it is <see cref="SrgbToLinear">linearized</see> first: the engine
	/// renders in linear (the backbuffer is an sRGB target that re-encodes the linear shader output on store), and
	/// premultiplied alpha-over is only correct in linear space. The shader applies the paper-white scale on HDR
	/// surfaces; on SDR the linear value round-trips back to the authored sRGB byte on store.
	/// </summary>
	public PremultipliedHdrColor ToPremultiplied(float opacity = 1f)
	{
		var a = A * opacity;
		return PremultipliedHdrColor.FromPremultiplied(SrgbToLinear(R) * a, SrgbToLinear(G) * a, SrgbToLinear(B) * a, a);
	}

	/// <summary>
	/// The sRGB EOTF (gamma decode), per channel. The standard piecewise curve extends monotonically past
	/// <c>1.0</c>, so an opt-in above-paper-white component (<c>&gt;1</c>) linearizes sensibly rather than clamping.
	/// </summary>
	private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}
