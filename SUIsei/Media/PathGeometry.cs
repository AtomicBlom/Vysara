using System.Text;
using SUIsei.Composition;

namespace SUIsei.Media;

/// <summary>
/// A geometry of any shape, as WinUI's <c>PathGeometry</c>: <see cref="Figures"/> of lines, Béziers and arcs, filled by
/// <see cref="FillRule"/>. In XAML it is written as figure elements, or as path markup where a property takes a
/// <see cref="Geometry"/> (<c>Data="M 0,0 L 10,0 10,10 Z"</c>), which <see cref="Geometry.Parse"/> also reads.
/// </summary>
[ContentProperty(Name = nameof(Figures))]
public sealed class PathGeometry : Geometry
{
	public static readonly DependencyProperty<PathGeometry, PathFigureCollection?> FiguresProperty =
		DependencyProperty.Register(nameof(Figures), new PropertyMetadata<PathGeometry, PathFigureCollection?>
		{
			PropertyChanged = static (geometry, oldValue, newValue) =>
			{
				oldValue?.RemoveListener(geometry);
				newValue?.AddListener(geometry);
				geometry.Changed();
			},
		});

	public static readonly DependencyProperty<PathGeometry, FillRule> FillRuleProperty =
		DependencyProperty.Register(nameof(FillRule), new PropertyMetadata<PathGeometry, FillRule> { PropertyChanged = OnChanged });

	/// <summary>The figures, each a connected outline.</summary>
	public PathFigureCollection Figures
	{
		get
		{
			if (GetValue(FiguresProperty) is { } figures)
				return figures;
			figures = [];
			SetValue(FiguresProperty, figures);
			return figures;
		}
		set => SetValue(FiguresProperty, value);
	}

	/// <summary>How overlapping figures and self-crossings decide what is inside. <see cref="FillRule.EvenOdd"/> by
	/// default.</summary>
	public FillRule FillRule { get => GetValue(FillRuleProperty); set => SetValue(FillRuleProperty, value); }

	private protected override void BuildPath(CompositionPath path)
	{
		path.FillRule = FillRule;
		if (GetValue(FiguresProperty) is not { } figures)
			return;
		for (var i = 0; i < figures.Count; i++)
			figures[i].AppendTo(path);
	}

	/// <summary>The geometry as WinUI path markup with absolute, round-trippable numbers, which
	/// <see cref="Geometry.Parse"/> reads back into an equal geometry. Markup cannot say that a figure is unfilled, so
	/// <see cref="PathFigure.IsFilled"/> is not written.</summary>
	internal string ToMarkup()
	{
		var sb = new StringBuilder();
		if (FillRule == FillRule.Nonzero)
			sb.Append("F1 ");
		if (GetValue(FiguresProperty) is { } figures)
		{
			for (var i = 0; i < figures.Count; i++)
			{
				if (i > 0)
					sb.Append(' ');
				figures[i].AppendMarkup(sb);
			}
		}
		return sb.ToString().TrimEnd();
	}
}
