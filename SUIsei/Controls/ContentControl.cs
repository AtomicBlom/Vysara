using System.Numerics;

namespace SUIsei.Controls;

/// <summary>
/// Hosts a single <see cref="Content"/> (PlanningUI.md §D2.1). Without a <see cref="Control.Template"/> it hosts the
/// content directly (a <see cref="UIElement"/> as-is; a string wrapped in a <see cref="TextBlock"/>) and draws an
/// optional solid <see cref="Background"/>. With a template, the control's visual tree comes from the template and a
/// <c>{TemplateBinding Content}</c> routes <see cref="Content"/> into the template's <see cref="ContentPresenter"/>;
/// <see cref="ContentChanged"/> lets that binding stay live.
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

			// No template: host the content directly (the original path, unchanged). With a template, the template's
			// ContentPresenter hosts it via a {TemplateBinding Content}; just notify so that binding re-reads.
			if (Template is null)
			{
				if (_hosted is not null)
					DropChild(_hosted);
				_hosted = ContentHelper.Wrap(value);
				if (_hosted is not null)
					AdoptChild(_hosted);
				InvalidateMeasure();
			}

			ContentChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>Raised when <see cref="Content"/> changes — a <c>{TemplateBinding Content}</c> subscribes to keep the
	/// template's <see cref="ContentPresenter"/> in sync.</summary>
	public event EventHandler? ContentChanged;

	public HdrColor? Background { get; set { if (field != value) { field = value; InvalidateContent(); } } }

	internal override IReadOnlyList<UIElement> VisualChildren =>
		Template is not null ? base.VisualChildren : (_hosted is null ? [] : [_hosted]);

	protected override Size MeasureOverride(Size available)
	{
		if (Template is not null)
			return base.MeasureOverride(available); // Control applies + measures the template

		if (_hosted is null)
			return Size.Zero;
		_hosted.Measure(available);
		return _hosted.DesiredSize;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		if (Template is not null)
			return base.ArrangeOverride(finalSize);

		_hosted?.Arrange(new Rect(0f, 0f, finalSize.Width, finalSize.Height));
		return finalSize;
	}

	private protected override VisualContent? BuildContent() =>
		Template is null && (AnimatedFill ?? Background) is { } bg
			? new RectContent(new Vector2(LayoutRect.Width, LayoutRect.Height), bg)
			: null;
}
