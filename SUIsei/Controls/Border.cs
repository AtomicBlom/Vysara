using System.Numerics;


namespace SUIsei.Controls;

/// <summary>
/// A single-child decorator with a background, a border, padding, and rounded corners (PlanningUI.md §D2.3):
/// measure deflates by border + padding around the child; arrange insets the child by the same; the visual draws a
/// (rounded) fill + border. Phase B treats <see cref="BorderThickness"/> as uniform for the drawn stroke.
/// </summary>
public sealed class Border : FrameworkElement
{
	public UIElement? Child
	{
		get;
		set
		{
			if (field == value)
				return;
			if (field is not null)
				DropChild(field);
			field = value;
			if (value is not null)
				AdoptChild(value);
			InvalidateMeasure();
		}
	}

	public HdrColor? Background { get; set { if (field != value) { field = value; InvalidateContent(); } } }
	public HdrColor? BorderBrush { get; set { if (field != value) { field = value; InvalidateContent(); } } }
	public Thickness BorderThickness { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }
	public Thickness Padding { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }
	public float CornerRadius { get; set { if (field != value) { field = value; InvalidateContent(); } } }

	internal override IReadOnlyList<UIElement> VisualChildren => Child is null ? [] : [Child];

	private Thickness Inset => new(
		BorderThickness.Left + Padding.Left, BorderThickness.Top + Padding.Top,
		BorderThickness.Right + Padding.Right, BorderThickness.Bottom + Padding.Bottom);

	protected override Size MeasureOverride(Size available)
	{
		var inset = Inset;
		if (Child is null)
			return new Size(inset.Horizontal, inset.Vertical);
		Child.Measure(new Size(MathF.Max(0f, available.Width - inset.Horizontal), MathF.Max(0f, available.Height - inset.Vertical)));
		return new Size(Child.DesiredSize.Width + inset.Horizontal, Child.DesiredSize.Height + inset.Vertical);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var inset = Inset;
		Child?.Arrange(new Rect(inset.Left, inset.Top,
			MathF.Max(0f, finalSize.Width - inset.Horizontal), MathF.Max(0f, finalSize.Height - inset.Vertical)));
		return finalSize;
	}

	private protected override VisualContent? BuildContent()
	{
		var fill = AnimatedFill ?? Background;
		if (fill is null && (BorderBrush is null || BorderThickness.Horizontal + BorderThickness.Vertical <= 0f))
			return null;
		return new RectContent(new Vector2(LayoutRect.Width, LayoutRect.Height), fill, CornerRadius, BorderBrush, BorderThickness.Left);
	}
}
