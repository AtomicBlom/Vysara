using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>Reading the colours brushes and composition paint carry, for assertions.</summary>
internal static class TestPaint
{
	/// <summary>The colour of a brush that must be a <see cref="SolidColorBrush"/>.</summary>
	public static HdrColor SolidColor(this Brush? brush) => brush.ShouldBeOfType<SolidColorBrush>().Color;

	/// <summary>The colour of composition paint that must be a <see cref="CompositionColorBrush"/>.</summary>
	public static HdrColor SolidColor(this CompositionBrush? paint) => paint.ShouldBeOfType<CompositionColorBrush>().Color;

	/// <summary>The colour an element's drawn content shows now: its rectangle's fill, or its glyphs' brush.</summary>
	public static HdrColor ShownColor(this UIElement element) => element.Visual.Content switch
	{
		RectContent rect => rect.Fill.SolidColor(),
		GlyphRunContent run => run.Brush.SolidColor(),
		var other => throw new ShouldAssertException($"{element.GetType().Name} draws {other?.GetType().Name ?? "nothing"}"),
	};
}
