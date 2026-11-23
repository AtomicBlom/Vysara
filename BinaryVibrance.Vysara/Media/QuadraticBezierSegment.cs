using System.Text;
using BinaryVibrance.Vysara.Composition;

namespace BinaryVibrance.Vysara.Media;

/// <summary>A quadratic Bézier curve to <see cref="Point2"/>, bent by the control point <see cref="Point1"/>, as
/// WinUI's <c>QuadraticBezierSegment</c>.</summary>
public sealed class QuadraticBezierSegment : PathSegment
{
	public static readonly DependencyProperty<QuadraticBezierSegment, Point> Point1Property =
		DependencyProperty.Register(nameof(Point1), new PropertyMetadata<QuadraticBezierSegment, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<QuadraticBezierSegment, Point> Point2Property =
		DependencyProperty.Register(nameof(Point2), new PropertyMetadata<QuadraticBezierSegment, Point> { PropertyChanged = OnChanged });

	/// <summary>The control point.</summary>
	public Point Point1 { get => GetValue(Point1Property); set => SetValue(Point1Property, value); }

	/// <summary>Where the curve ends.</summary>
	public Point Point2 { get => GetValue(Point2Property); set => SetValue(Point2Property, value); }

	internal override void AppendTo(CompositionPath path) => path.QuadraticTo(Point1.ToVector2(), Point2.ToVector2());

	internal override void AppendMarkup(StringBuilder sb)
	{
		sb.Append(" Q");
		AppendPoint(sb, Point1);
		AppendPoint(sb, Point2);
	}
}
