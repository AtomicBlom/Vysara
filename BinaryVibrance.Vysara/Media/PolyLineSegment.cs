using System.Text;
using BinaryVibrance.Vysara.Composition;

namespace BinaryVibrance.Vysara.Media;

/// <summary>Straight lines through each of <see cref="Points"/> in turn, as WinUI's <c>PolyLineSegment</c>.</summary>
public sealed class PolyLineSegment : PathSegment
{
	public static readonly DependencyProperty<PolyLineSegment, PointCollection?> PointsProperty =
		DependencyProperty.Register(nameof(Points), new PropertyMetadata<PolyLineSegment, PointCollection?> { PropertyChanged = OnPointsChanged });

	/// <summary>The points the lines run through, after the segment's start.</summary>
	public PointCollection Points { get => GetOrCreatePoints(PointsProperty); set => SetValue(PointsProperty, value); }

	internal override void AppendTo(CompositionPath path)
	{
		if (GetValue(PointsProperty) is not { } points)
			return;
		foreach (var p in points.AsSpan())
			path.LineTo(p.ToVector2());
	}

	internal override void AppendMarkup(StringBuilder sb)
	{
		if (GetValue(PointsProperty) is not { Count: > 0 } points)
			return;
		sb.Append(" L");
		AppendPoints(sb, points.AsSpan());
	}
}
