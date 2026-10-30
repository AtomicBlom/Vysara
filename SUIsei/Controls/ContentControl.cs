using SUIsei.Composition;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// Hosts a single <see cref="Content"/>. Without a <see cref="Control.Template"/> it hosts the
/// content directly (a <see cref="UIElement"/> as-is; a string wrapped in a <see cref="TextBlock"/>) and paints
/// <see cref="Control.Background"/> behind it. With a template, the control's visual tree comes from the template and a
/// <c>{TemplateBinding Content}</c> (<see cref="RegisterContentTemplateBinding"/>) routes <see cref="Content"/> into the
/// template's <see cref="ContentPresenter"/>, live, until the template is replaced. Either way the text it shows inherits
/// <see cref="Control.Foreground"/> and <see cref="Control.FontSize"/> unless something between sets its own.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public class ContentControl : Control
{
	private UIElement? _hosted;

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

			// No template: host the content directly. With a template, the template's ContentPresenter hosts it through
			// the {TemplateBinding Content} its factory registered, which is pushed the new content.
			if (Template is null)
			{
				if (_hosted is not null)
					DropChild(_hosted);
				_hosted = ContentHelper.Wrap(value);
				_hostedList = _hosted is null ? [] : [_hosted];
				if (_hosted is not null)
					AdoptChild(_hosted);
				InvalidateMeasure();
			}

			_contentTemplateTargets?.Invoke(value);
			ContentChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>Raised when <see cref="Content"/> changes. A template wires its <c>{TemplateBinding Content}</c> with
	/// <see cref="RegisterContentTemplateBinding"/> instead, which is dropped with the template.</summary>
	public event EventHandler? ContentChanged;

	/// <summary>Wire a OneWay <c>{TemplateBinding Content}</c> from <see cref="Content"/> to a template part, usually its
	/// <see cref="ContentPresenter"/>: <paramref name="push"/> is called now with the content and on every later change. As
	/// <see cref="DependencyObject.RegisterTemplateBinding{TOwner, TValue}(DependencyProperty{TOwner, TValue}, Action{TValue})"/>,
	/// it belongs to the template: when the control's template is replaced it is dropped, so the old parts hear nothing more
	/// and can be collected. Public so a generated (or hand-written) template factory can wire it.</summary>
	public void RegisterContentTemplateBinding(Action<object?> push)
	{
		_contentTemplateTargets += push;
		push(Content);
	}

	// The {TemplateBinding Content} pushes the template's factory registered, called before ContentChanged is raised.
	private Action<object?>? _contentTemplateTargets;

	internal override void ClearTemplateBindings()
	{
		_contentTemplateTargets = null;
		base.ClearTemplateBindings();
	}

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

	private protected override VisualContent? UpdateContent() =>
		Template is null && BackgroundPaint(Background) is { } bg ? RetainedRect(bg) : null;
}
