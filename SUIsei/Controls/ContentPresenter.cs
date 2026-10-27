using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// Presents a single <see cref="Content"/> — the lightweight presenter a control template
/// points at. When a <see cref="ContentTemplate"/> is set, the content is realized through that
/// <see cref="DataTemplate"/>; otherwise a <see cref="UIElement"/> is hosted as-is and any other value is wrapped in
/// a <see cref="TextBlock"/> (the shared <see cref="ContentHelper"/> policy), which <see cref="Foreground"/> colours. An
/// optional <see cref="Background"/> lets it stand in for a panel.
/// </summary>
public class ContentPresenter : FrameworkElement
{
	public static readonly DependencyProperty<ContentPresenter, Brush?> BackgroundProperty =
		DependencyProperty.Register(nameof(Background),
			new PropertyMetadata<ContentPresenter, Brush?>
			{
				PropertyChanged = static (p, old, @new) =>
				{
					p.TransitionBackground(p.BackgroundTransition, old, @new);
					p.OnBrushChanged(old, @new);
				},
			});

	/// <summary>The brush for the text the presenter makes for content that is not an element; null leaves that text's
	/// own default.</summary>
	public static readonly DependencyProperty<ContentPresenter, Brush?> ForegroundProperty =
		DependencyProperty.Register(nameof(Foreground),
			new PropertyMetadata<ContentPresenter, Brush?>
			{
				PropertyChanged = static (p, _, value) =>
				{
					if (p._wrapped is { } text)
						text.Foreground = value;
				},
			});

	private UIElement? _hosted;

	// The text block made for content that is not an element, which Foreground colours; null when the content is an
	// element or comes through a template.
	private TextBlock? _wrapped;

	// VisualChildren, rebuilt only when _hosted changes, so reading it allocates nothing.
	private UIElement[] _hostedList = [];

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

	public Brush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

	/// <summary>The brush for the text made for content that is not an element (a string).</summary>
	public Brush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

	/// <summary>How a change of <see cref="Background"/> animates; null shows it at once.</summary>
	public BrushTransition? BackgroundTransition { get; set; }

	internal override IReadOnlyList<UIElement> VisualChildren => _hostedList;

	private void Rehost()
	{
		if (_hosted is not null)
			DropChild(_hosted);
		_hosted = ContentTemplate is { } dt ? dt.Factory(Content) : ContentHelper.Wrap(Content);
		_wrapped = ContentTemplate is null && Content is not UIElement ? _hosted as TextBlock : null;
		if (_wrapped is not null && Foreground is { } foreground)
			_wrapped.Foreground = foreground;
		_hostedList = _hosted is null ? [] : [_hosted];
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

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Background, attached);
		base.OnLiveTreeChanged(attached);
	}

	private protected override VisualContent? UpdateContent() => BackgroundPaint(Background) is { } bg ? RetainedRect(bg) : null;
}
