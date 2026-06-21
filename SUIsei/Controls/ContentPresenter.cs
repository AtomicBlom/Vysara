using System.Numerics;

namespace SUIsei.Controls;

/// <summary>
/// Presents a single <see cref="Content"/> (PlanningUI.md §D2.1) — the lightweight presenter a control template
/// points at. When a <see cref="ContentTemplate"/> is set, the content is realized through that
/// <see cref="DataTemplate"/>; otherwise a <see cref="UIElement"/> is hosted as-is and any other value is wrapped in
/// a <see cref="TextBlock"/> (the shared <see cref="ContentHelper"/> policy). An optional solid
/// <see cref="Background"/> lets it stand in for a panel.
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
			Rehost();
		}
	}

	/// <summary>When set, <see cref="Content"/> is realized through this template instead of the default wrapping.</summary>
	public DataTemplate? ContentTemplate
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			field = value;
			Rehost();
		}
	}

	public HdrColor? Background { get; set { if (field != value) { field = value; InvalidateContent(); } } }

	internal override IReadOnlyList<UIElement> VisualChildren => _hosted is null ? [] : [_hosted];

	private void Rehost()
	{
		if (_hosted is not null)
			DropChild(_hosted);
		_hosted = ContentTemplate is { } dt ? dt.Factory(Content) : ContentHelper.Wrap(Content);
		if (_hosted is not null)
			AdoptChild(_hosted);
		InvalidateMeasure();
	}

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
