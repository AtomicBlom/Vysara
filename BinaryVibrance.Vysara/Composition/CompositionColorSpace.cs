namespace BinaryVibrance.Vysara.Composition;

/// <summary>The space a gradient interpolates its colours in, as WinUI's <c>CompositionColorSpace</c> (its RGB
/// members).</summary>
public enum CompositionColorSpace
{
	/// <summary>The authored, sRGB-encoded values. Defined only within paper white, so a stop above 1 is an error.</summary>
	Rgb,

	/// <summary>Linear light (scRGB), which holds colours above paper white.</summary>
	RgbLinear,
}
