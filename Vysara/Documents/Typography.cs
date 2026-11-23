using Vysara.Controls;

namespace Vysara.Documents;

/// <summary>
/// WinUI's OpenType typography settings, as attached properties on the text they shape:
/// <c>&lt;TextBlock Typography.NumeralAlignment="Tabular" /&gt;</c>. Of WinUI's set, <c>NumeralAlignment</c> is here, set
/// on a <see cref="TextBlock"/>; elsewhere it is VYSARA004, as Vysara has no inherited text properties yet. The text
/// measures and draws with the features a setting names, so layout reserves the width the run draws at.
/// </summary>
public static class Typography
{
	/// <summary>How digits' widths align: <see cref="FontNumeralAlignment.Tabular"/> shapes with <c>tnum</c>, so columns of
	/// numbers line up and a changing number keeps its width; <see cref="FontNumeralAlignment.Proportional"/> with
	/// <c>pnum</c>. <see cref="FontNumeralAlignment.Normal"/> by default, the font's own digits.</summary>
	public static readonly DependencyProperty<TextBlock, FontNumeralAlignment> NumeralAlignmentProperty =
		DependencyProperty.RegisterAttached("NumeralAlignment",
			new PropertyMetadata<TextBlock, FontNumeralAlignment> { Invalidates = InvalidationFlags.Measure });

	public static void SetNumeralAlignment(TextBlock element, FontNumeralAlignment value) => element.SetValue(NumeralAlignmentProperty, value);
	public static FontNumeralAlignment GetNumeralAlignment(TextBlock element) => element.GetValue(NumeralAlignmentProperty);

	/// <summary>The OpenType features <paramref name="element"/>'s typography settings name, one shared set per
	/// combination.</summary>
	internal static FontFeatureSet FeaturesOf(TextBlock element) => GetNumeralAlignment(element) switch
	{
		FontNumeralAlignment.Tabular => FontFeatureSet.TabularFigures,
		FontNumeralAlignment.Proportional => FontFeatureSet.ProportionalFigures,
		_ => FontFeatureSet.Empty,
	};
}
