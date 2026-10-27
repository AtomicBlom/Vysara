using SUIsei.Media;

namespace SUIsei.Shapes;

/// <summary>
/// Straight lines through <see cref="Points"/> in turn, as WinUI's <c>Polyline</c>: an open outline, filled as if it
/// were closed. In XAML, <c>Points="0,0 10,5 20,0"</c>. A change to the points applies in place; from code,
/// <see cref="PointCollection.SetAll"/> replaces them all with one change and no allocation.
/// </summary>
public sealed class Polyline : Shape
{
	public static readonly DependencyProperty<Polyline, PointCollection?> PointsProperty =
		DependencyProperty.Register(nameof(Points), new PropertyMetadata<Polyline, PointCollection?>
		{
			PropertyChanged = static (shape, oldValue, newValue) =>
			{
				if (shape.AttachedRoot is not null)
				{
					PointOutline.Listen(shape, oldValue, false);
					PointOutline.Listen(shape, newValue, true);
				}
				shape._outline.Invalidate();
				shape.OnOutlineChanged();
			},
		});

	public static readonly DependencyProperty<Polyline, FillRule> FillRuleProperty =
		DependencyProperty.Register(nameof(FillRule), new PropertyMetadata<Polyline, FillRule>
		{
			PropertyChanged = static (shape, _, _) =>
			{
				shape._outline.Invalidate();
				shape.InvalidateContent();
			},
		});

	private readonly PointOutline _outline = new(closed: false);

	/// <summary>The points the lines run through, in the shape's own coordinates.</summary>
	public PointCollection Points
	{
		get
		{
			if (GetValue(PointsProperty) is { } points)
				return points;
			points = [];
			SetValue(PointsProperty, points);
			return points;
		}
		set => SetValue(PointsProperty, value);
	}

	/// <summary>How a fill treats the places the lines cross. <see cref="FillRule.EvenOdd"/> by default.</summary>
	public FillRule FillRule { get => GetValue(FillRuleProperty); set => SetValue(FillRuleProperty, value); }

	private protected override CompositionPath DefiningPath => _outline.Build(GetValue(PointsProperty), FillRule);

	private protected override void OnLiveTreeChanged(bool attached)
	{
		PointOutline.Listen(this, GetValue(PointsProperty), attached);
		base.OnLiveTreeChanged(attached);
	}

	private protected override void OnSubPropertyChanged(object source)
	{
		if (!ReferenceEquals(source, GetValue(PointsProperty)))
		{
			base.OnSubPropertyChanged(source);
			return;
		}
		_outline.Invalidate();
		OnOutlineChanged();
	}
}
