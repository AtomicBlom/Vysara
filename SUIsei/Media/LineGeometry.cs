using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>A straight line from <see cref="StartPoint"/> to <see cref="EndPoint"/>, as WinUI's <c>LineGeometry</c>.
/// It encloses nothing, so it only draws when stroked.</summary>
public sealed class LineGeometry : Geometry
{
	public static readonly DependencyProperty<LineGeometry, Point> StartPointProperty =
		DependencyProperty.Register(nameof(StartPoint), new PropertyMetadata<LineGeometry, Point> { PropertyChanged = OnChanged });

	public static readonly DependencyProperty<LineGeometry, Point> EndPointProperty =
		DependencyProperty.Register(nameof(EndPoint), new PropertyMetadata<LineGeometry, Point> { PropertyChanged = OnChanged });

	public Point StartPoint { get => GetValue(StartPointProperty); set => SetValue(StartPointProperty, value); }

	public Point EndPoint { get => GetValue(EndPointProperty); set => SetValue(EndPointProperty, value); }

	private protected override void BuildPath(CompositionPath path)
	{
		path.MoveTo(StartPoint.ToVector2(), filled: false);
		path.LineTo(EndPoint.ToVector2());
	}
}
