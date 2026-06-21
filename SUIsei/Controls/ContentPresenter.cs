using System.Numerics;

namespace SUIsei.Controls;

/// <summary>
/// Presents a single <see cref="Content"/> (PlanningUI.md §D2.1) — a <see cref="UIElement"/> hosted directly, a
/// string wrapped in a <see cref="TextBlock"/> (shared <see cref="ContentHelper"/> policy). Unlike
/// <see cref="ContentControl"/> it is the lightweight presenter a control template points at; an optional solid
/// <see cref="Background"/> lets it stand in for a panel. (Content/data templates land in MU4.)
/// </summary>
public class ContentPresenter : FrameworkElement
{
	private UIElement? _hosted;

	public object? Content
	{
		get;
		set
		{
			field = value;
			if (_hosted is not null)
				DropChild(_hosted);
			_hosted = ContentHelper.Wrap(value);
			if (_hosted is not null)
				AdoptChild(_hosted);
			InvalidateMeasure();
		}
	}

	public HdrColor? Background { get; set { if (field != value) { field = value; InvalidateContent(); } } }

	internal override IReadOnlyList<UIElement> VisualChildren => _hosted is null ? [] : [_hosted];

	protected override Size MeasureOverride(Size available)
	{
		if (_hosted is null)
			return Size.Zero;
		_hosted.Measure(available);
		return _hosted.DesiredSize;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		_hosted?.Arrange(new Rect(0f, 0f, finalSize.Width, finalSize.Height));
		return finalSize;
	}

	private protected override VisualContent? BuildContent() =>
		Background is { } bg ? new RectContent(new Vector2(LayoutRect.Width, LayoutRect.Height), bg) : null;
}
