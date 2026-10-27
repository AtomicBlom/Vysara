using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// Hosts a single <see cref="Content"/>. Without a <see cref="Control.Template"/> it hosts the
/// content directly (a <see cref="UIElement"/> as-is; a string wrapped in a <see cref="TextBlock"/>, which
/// <see cref="Control.Foreground"/> colours) and paints <see cref="Control.Background"/> behind it. With a template, the
/// control's visual tree comes from the template and a <c>{TemplateBinding Content}</c> routes <see cref="Content"/> into
/// the template's <see cref="ContentPresenter"/>; <see cref="ContentChanged"/> lets that binding stay live.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public class ContentControl : Control
{
	private UIElement? _hosted;

	// The text block made for content that is not an element, which Foreground colours; null otherwise.
	private TextBlock? _wrapped;

	// VisualChildren without a template, rebuilt only when _hosted changes, so reading it allocates nothing.
	private UIElement[] _hostedList = [];

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
				_wrapped = value is UIElement ? null : _hosted as TextBlock;
				if (_wrapped is not null && Foreground is { } foreground)
					_wrapped.Foreground = foreground;
				_hostedList = _hosted is null ? [] : [_hosted];
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

	/// <summary>How a change of the background this control paints itself (with no template) animates; null shows it at
	/// once. A control's own look sets it, as <see cref="ListViewItem"/> does for its selection.</summary>
	internal BrushTransition? BackgroundTransition { get; set; }

	internal override IReadOnlyList<UIElement> VisualChildren => Template is not null ? base.VisualChildren : _hostedList;

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

	private protected override void OnBackgroundChanged(Brush? old, Brush? @new)
	{
		TransitionBackground(BackgroundTransition, old, @new);
		base.OnBackgroundChanged(old, @new);
	}

	private protected override void OnForegroundChanged(Brush? foreground)
	{
		if (_wrapped is { } text)
			text.Foreground = foreground;
	}

	private protected override VisualContent? UpdateContent() =>
		Template is null && BackgroundPaint(Background) is { } bg ? RetainedRect(bg) : null;
}
