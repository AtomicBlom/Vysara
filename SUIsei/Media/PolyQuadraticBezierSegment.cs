using System.Text;

namespace SUIsei.Media;

/// <summary>Quadratic Bézier curves, one per two of <see cref="Points"/> (a control point and an end point), as
/// WinUI's <c>PolyQuadraticBezierSegment</c>. A point left over after the last whole pair draws nothing.</summary>
public sealed class PolyQuadraticBezierSegment : PathSegment
{
	public static readonly DependencyProperty<PolyQuadraticBezierSegment, PointCollection?> PointsProperty =
		DependencyProperty.Register(nameof(Points), new PropertyMetadata<PolyQuadraticBezierSegment, PointCollection?> { PropertyChanged = OnPointsChanged });

	/// <summary>The curves' control and end points, two per curve.</summary>
	public PointCollection Points { get => GetOrCreatePoints(PointsProperty); set => SetValue(PointsProperty, value); }

	internal override void AppendTo(CompositionPath path)
	{
		if (GetValue(PointsProperty) is not { } collection)
			return;
		var points = collection.AsSpan();
		for (var i = 0; i + 1 < points.Length; i += 2)
			path.QuadraticTo(points[i].ToVector2(), points[i + 1].ToVector2());
	}

	internal override void AppendMarkup(StringBuilder sb)
	{
		if (GetValue(PointsProperty) is not { Count: >= 2 } collection)
			return;
		var points = collection.AsSpan();
		sb.Append(" Q");
		AppendPoints(sb, points[..(points.Length / 2 * 2)]);
	}
}
