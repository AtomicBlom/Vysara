using Vysara.Media;
using Vysara.Shapes;
using Path = Vysara.Shapes.Path;

namespace Vysara.Controls.Primitives;

/// <summary>The small stroked marks built-in looks draw (a chevron, a cross, an arrow), each a <see cref="Path"/> of
/// rounded strokes in a box of its own size.</summary>
internal static class Marks
{
	/// <summary>A chevron pointing down, in a box <paramref name="size"/> wide.</summary>
	public static Path ChevronDown(float size, Brush stroke) => Mark(FormattableString.Invariant($"M 0,{size * 0.3f} L {size / 2f},{size * 0.75f} L {size},{size * 0.3f}"), size, stroke);

	/// <summary>A chevron pointing up, in a box <paramref name="size"/> wide.</summary>
	public static Path ChevronUp(float size, Brush stroke) => Mark(FormattableString.Invariant($"M 0,{size * 0.7f} L {size / 2f},{size * 0.25f} L {size},{size * 0.7f}"), size, stroke);

	/// <summary>A cross, in a box <paramref name="size"/> wide.</summary>
	public static Path Cross(float size, Brush stroke) => Mark(FormattableString.Invariant($"M 0,0 L {size},{size} M {size},0 L 0,{size}"), size, stroke);

	private static Path Mark(string markup, float size, Brush stroke) => new()
	{
		Data = Geometry.Parse(markup),
		Width = size,
		Height = size,
		Stroke = stroke,
		StrokeThickness = 1.5f,
		StrokeStartLineCap = PenLineCap.Round,
		StrokeEndLineCap = PenLineCap.Round,
		StrokeLineJoin = PenLineJoin.Round,
		IsHitTestVisible = false,
		VerticalAlignment = VerticalAlignment.Center,
		HorizontalAlignment = HorizontalAlignment.Center,
	};
}
