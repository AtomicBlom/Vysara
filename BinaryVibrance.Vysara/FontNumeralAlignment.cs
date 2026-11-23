namespace BinaryVibrance.Vysara;

/// <summary>How the widths of digits are aligned, set by <see cref="Documents.Typography.NumeralAlignmentProperty"/>: WinUI's
/// <c>FontNumeralAlignment</c>.</summary>
public enum FontNumeralAlignment
{
	/// <summary>The font's own digits.</summary>
	Normal = 0,

	/// <summary>Digits of their own widths (OpenType <c>pnum</c>).</summary>
	Proportional = 1,

	/// <summary>Digits of equal advance, so columns of numbers line up (OpenType <c>tnum</c>).</summary>
	Tabular = 2,
}
