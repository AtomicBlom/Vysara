namespace SUIsei.Controls;

/// <summary>
/// Stacks children end-to-end along <see cref="Orientation"/> (PlanningUI.md §D2.3): the stacking axis is measured
/// unconstrained (∞), desired = sum along it and max across it; arrange lays children out one after another, each
/// stretched on the cross axis.
/// </summary>
public sealed class StackPanel : Panel
{
	public Orientation Orientation { get; set { if (field != value) { field = value; InvalidateMeasure(); } } } = Orientation.Vertical;

	protected override Size MeasureOverride(Size available)
	{
		var vertical = Orientation == Orientation.Vertical;
		var childAvail = vertical
			? available with { Height = float.PositiveInfinity }
			: available with { Width = float.PositiveInfinity };

		float main = 0f, cross = 0f;
		foreach (var child in Children.Items)
		{
			child.Measure(childAvail);
			if (vertical)
			{
				main += child.DesiredSize.Height;
				cross = MathF.Max(cross, child.DesiredSize.Width);
			}
			else
			{
				main += child.DesiredSize.Width;
				cross = MathF.Max(cross, child.DesiredSize.Height);
			}
		}
		return vertical ? new Size(cross, main) : new Size(main, cross);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var vertical = Orientation == Orientation.Vertical;
		float offset = 0f;
		foreach (var child in Children.Items)
		{
			if (vertical)
			{
				var h = child.DesiredSize.Height;
				child.Arrange(new Rect(0f, offset, finalSize.Width, h));
				offset += h;
			}
			else
			{
				var w = child.DesiredSize.Width;
				child.Arrange(new Rect(offset, 0f, w, finalSize.Height));
				offset += w;
			}
		}
		return finalSize;
	}
}
