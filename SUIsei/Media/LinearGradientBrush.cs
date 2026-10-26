using System.Numerics;

namespace SUIsei.Media;

/// <summary>
/// Paints a gradient along the line from <see cref="StartPoint"/> to <see cref="EndPoint"/>, as WinUI's
/// <c>LinearGradientBrush</c>: the colour is constant across the line, and the stops' offsets measure along it. The
/// default line runs from the top left (0,0) to the bottom right (1,1) of the area painted. With
/// <see cref="BrushMappingMode.RelativeToBoundingBox"/> that area is the element's: a border's rectangle, or a text block's
/// layout box for its text. If the start and end coincide, the last stop's colour fills the area.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush
{
	public static readonly DependencyProperty<LinearGradientBrush, Point> StartPointProperty =
		DependencyProperty.Register(nameof(StartPoint),
			new PropertyMetadata<LinearGradientBrush, Point>
			{
				PropertyChanged = static (b, _, value) =>
				{
					b.Linear.StartPoint = value.ToVector2();
					b.Changed();
				},
			});

	public static readonly DependencyProperty<LinearGradientBrush, Point> EndPointProperty =
		DependencyProperty.Register(nameof(EndPoint),
			new PropertyMetadata<LinearGradientBrush, Point>
			{
				DefaultValue = new Point(1f, 1f),
				PropertyChanged = static (b, _, value) =>
				{
					b.Linear.EndPoint = value.ToVector2();
					b.Changed();
				},
			});

	public LinearGradientBrush() : base(new CompositionLinearGradientBrush { EndPoint = Vector2.One })
	{
	}

	/// <summary>Where offset 0 lies; animatable with a <c>PointAnimation</c>.</summary>
	public Point StartPoint { get => GetValue(StartPointProperty); set => SetValue(StartPointProperty, value); }

	/// <summary>Where offset 1 lies; animatable with a <c>PointAnimation</c>.</summary>
	public Point EndPoint { get => GetValue(EndPointProperty); set => SetValue(EndPointProperty, value); }

	private CompositionLinearGradientBrush Linear => (CompositionLinearGradientBrush)CompositionBrush;
}
