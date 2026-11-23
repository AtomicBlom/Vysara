using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Shapes;

/// <summary>
/// A closed outline through <see cref="Points"/> in turn, as WinUI's <c>Polygon</c>: the last point joins the first.
/// In XAML, <c>Points="0,0 10,5 20,0"</c>. A change to the points applies in place; from code,
/// <see cref="PointCollection.SetAll"/> replaces them all with one change and no allocation.
/// </summary>
public sealed class Polygon : Shape
{
	public static readonly DependencyProperty<Polygon, PointCollection?> PointsProperty =
		DependencyProperty.Register(nameof(Points), new PropertyMetadata<Polygon, PointCollection?>
		{
			PropertyChanged = static (shape, oldValue, newValue) =>
			{
				if (shape.AttachedRoot is not null)
				{
					PointOutline.Listen(shape, oldValue, false);
					PointOutline.Listen(shape, newValue, true);
				}
				shape.OnOutlineChanged();
			},
		});

	public static readonly DependencyProperty<Polygon, FillRule> FillRuleProperty =
		DependencyProperty.Register(nameof(FillRule), new PropertyMetadata<Polygon, FillRule>
		{
			PropertyChanged = static (shape, _, _) => shape.InvalidateContent(),
		});

	private readonly PointOutline _outline = new(closed: true);

	/// <summary>The corners of the outline, in the shape's own coordinates.</summary>
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

	/// <summary>How a fill treats the places the outline crosses itself. <see cref="FillRule.EvenOdd"/> by
	/// default.</summary>
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
		OnOutlineChanged();
	}
}
