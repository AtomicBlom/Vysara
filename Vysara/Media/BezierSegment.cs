using System.Text;
using Vysara.Composition;

namespace Vysara.Media;

/// <summary>A cubic Bézier curve to <see cref="Point3"/>, bent by the control points <see cref="Point1"/> and
/// <see cref="Point2"/>, as WinUI's <c>BezierSegment</c>.</summary>
public sealed class BezierSegment : PathSegment
{
	public static readonly DependencyProperty<BezierSegment, Point> Point1Property =
		DependencyProperty.Register(nameof(Point1), new PropertyMetadata<BezierSegment, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<BezierSegment, Point> Point2Property =
		DependencyProperty.Register(nameof(Point2), new PropertyMetadata<BezierSegment, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<BezierSegment, Point> Point3Property =
		DependencyProperty.Register(nameof(Point3), new PropertyMetadata<BezierSegment, Point> { PropertyChanged = OnChanged });

	/// <summary>The first control point.</summary>
	public Point Point1 { get => GetValue(Point1Property); set => SetValue(Point1Property, value); }

	/// <summary>The second control point.</summary>
	public Point Point2 { get => GetValue(Point2Property); set => SetValue(Point2Property, value); }

	/// <summary>Where the curve ends.</summary>
	public Point Point3 { get => GetValue(Point3Property); set => SetValue(Point3Property, value); }

	internal override void AppendTo(CompositionPath path) => path.CubicTo(Point1.ToVector2(), Point2.ToVector2(), Point3.ToVector2());

	internal override void AppendMarkup(StringBuilder sb)
	{
		sb.Append(" C");
		AppendPoint(sb, Point1);
		AppendPoint(sb, Point2);
		AppendPoint(sb, Point3);
	}
}
