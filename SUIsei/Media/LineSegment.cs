using System.Text;

namespace SUIsei.Media;

/// <summary>A straight line to <see cref="Point"/>, as WinUI's <c>LineSegment</c>.</summary>
public sealed class LineSegment : PathSegment
{
	public static readonly DependencyProperty<LineSegment, Point> PointProperty =
		DependencyProperty.Register(nameof(Point), new PropertyMetadata<LineSegment, Point> { PropertyChanged = OnChanged });

	/// <summary>Where the line ends.</summary>
	public Point Point { get => GetValue(PointProperty); set => SetValue(PointProperty, value); }

	internal override void AppendTo(CompositionPath path) => path.LineTo(Point.ToVector2());

	internal override void AppendMarkup(StringBuilder sb)
	{
		sb.Append(" L");
		AppendPoint(sb, Point);
	}
}
