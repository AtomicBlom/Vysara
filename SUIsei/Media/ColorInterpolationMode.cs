namespace SUIsei.Media;

/// <summary>The space a gradient's colours interpolate in, as WinUI's <c>ColorInterpolationMode</c>.</summary>
public enum ColorInterpolationMode
{
	/// <summary>The authored, sRGB-encoded values: WinUI's default. Defined only within paper white, so a gradient with a
	/// stop above 1 throws when it is drawn. Drawn exactly at nine points per stop interval (its ends and seven between)
	/// and interpolated in linear light between them: within 0.006 in linear light; see
	/// <see cref="CompositionGradientBrush.SrgbSubBands"/>.</summary>
	SRgbLinearInterpolation,

	/// <summary>Linear light (scRGB). Exact, and holds stops above paper white.</summary>
	ScRgbLinearInterpolation,
}
