using System.Text;
using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>Cubic Bézier curves, one per three of <see cref="Points"/> (two control points and an end point), as
/// WinUI's <c>PolyBezierSegment</c>. Points left over after the last whole three draw nothing.</summary>
public sealed class PolyBezierSegment : PathSegment
{
	public static readonly DependencyProperty<PolyBezierSegment, PointCollection?> PointsProperty =
		DependencyProperty.Register(nameof(Points), new PropertyMetadata<PolyBezierSegment, PointCollection?> { PropertyChanged = OnPointsChanged });

	/// <summary>The curves' control and end points, three per curve.</summary>
	public PointCollection Points { get => GetOrCreatePoints(PointsProperty); set => SetValue(PointsProperty, value); }

	internal override void AppendTo(CompositionPath path)
	{
		if (GetValue(PointsProperty) is not { } collection)
			return;
		var points = collection.AsSpan();
		for (var i = 0; i + 2 < points.Length; i += 3)
			path.CubicTo(points[i].ToVector2(), points[i + 1].ToVector2(), points[i + 2].ToVector2());
	}

	internal override void AppendMarkup(StringBuilder sb)
	{
		if (GetValue(PointsProperty) is not { Count: >= 3 } collection)
			return;
		var points = collection.AsSpan();
		sb.Append(" C");
		AppendPoints(sb, points[..(points.Length / 3 * 3)]);
	}
}
