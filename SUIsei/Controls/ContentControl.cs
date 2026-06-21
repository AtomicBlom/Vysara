using System.Numerics;

namespace SUIsei.Controls;

/// <summary>
/// Hosts a single <see cref="Content"/> (PlanningUI.md §D2.1): a <see cref="UIElement"/> is hosted directly; a
/// string (or any other object) is wrapped in a <see cref="TextBlock"/>. Optional solid <see cref="Background"/>.
/// (A real <c>ContentPresenter</c> + templating lands in MU4.)
/// </summary>
public class ContentControl : Control
{
	private UIElement? _hosted;

	public object? Content
	{
		get;
		set
		{
			// Re-hosting rebuilds the child and invalidates layout, discarding its state — skip it when the content
			// is unchanged (value equality, so a binding refresh that pushes an equal string does not rebuild).
			if (Equals(field, value))
				return;
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
