using SUIsei.Composition;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// Presents a single <see cref="Content"/> — the lightweight presenter a control template
/// points at. When a <see cref="ContentTemplate"/> is set, the content is realized through that
/// <see cref="DataTemplate"/>; otherwise a <see cref="UIElement"/> is hosted as-is and any other value is wrapped in
/// a <see cref="TextBlock"/> (the shared <see cref="ContentHelper"/> policy). <see cref="Foreground"/> and
/// <see cref="FontSize"/> are inherited, as in WinUI, by that text and by any text in the content, unless it sets its own;
/// unset here, the presenter passes on the values from above it. An optional <see cref="Background"/> lets it stand in for
/// a panel.
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

	/// <summary>The brush for the text the presenter shows, inherited as in WinUI: the text it makes for content that is not
	/// an element, and the text in element content or a content template, is painted with it unless that text sets its own.
	/// Null leaves that text the brush from above.</summary>
	public static readonly DependencyProperty<ContentPresenter, Brush?> ForegroundProperty =
		DependencyProperty.Register(nameof(Foreground),
			new PropertyMetadata<ContentPresenter, Brush?>
			{
				Inherits = InheritedText.Foreground,
				Validate = static (_, brush) => InheritedText.ValidateForeground(brush),
			});

	/// <summary>The size of the text the presenter shows, in DIPs, inherited as <see cref="Foreground"/> is.</summary>
	public static readonly DependencyProperty<ContentPresenter, float> FontSizeProperty =
		DependencyProperty.Register(nameof(FontSize),
			new PropertyMetadata<ContentPresenter, float> { DefaultValue = 14f, Inherits = InheritedText.FontSize });

	private UIElement? _hosted;

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

	/// <summary>The brush for the text the presenter shows: null unless set here or inherited from above.</summary>
	public Brush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

	/// <summary>The size of the text the presenter shows, in DIPs: 14 unless set here or inherited from above.</summary>
	public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

	/// <summary>How a change of <see cref="Background"/> animates; null shows it at once.</summary>
	public BrushTransition? BackgroundTransition { get; set; }

	internal override IReadOnlyList<UIElement> VisualChildren => _hostedList;

	private void Rehost()
	{
		if (_hosted is not null)
			DropChild(_hosted);
		_hosted = ContentTemplate is { } dt ? dt.Factory(Content) : ContentHelper.Wrap(Content);
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
