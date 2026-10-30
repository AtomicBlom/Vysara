using SUIsei.Composition;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A single-child decorator with a background, a border, padding, and rounded corners:
/// measure deflates by border + padding around the child; arrange insets the child by the same; the visual draws a
/// (rounded) fill + border. <see cref="BorderThickness"/> is treated as uniform for the drawn stroke; each corner of
/// <see cref="CornerRadius"/> has its own radius.
/// </summary>
[ContentProperty(Name = nameof(Child))]
public sealed class Border : FrameworkElement
{
	public static readonly DependencyProperty<Border, Brush?> BackgroundProperty =
		DependencyProperty.Register(nameof(Background),
			new PropertyMetadata<Border, Brush?>
			{
				PropertyChanged = static (b, old, @new) =>
				{
					b.TransitionBackground(b.BackgroundTransition, old, @new);
					b.OnBrushChanged(old, @new);
				},
			});

	public static readonly DependencyProperty<Border, Brush?> BorderBrushProperty =
		DependencyProperty.Register(nameof(BorderBrush),
			new PropertyMetadata<Border, Brush?> { PropertyChanged = static (b, old, @new) => b.OnBrushChanged(old, @new) });

	/// <summary>The radius of each corner. Radii too large for the border are scaled down together until the corners on
	/// every side fit along it.</summary>
	public static readonly DependencyProperty<Border, CornerRadius> CornerRadiusProperty =
		DependencyProperty.Register(nameof(CornerRadius),
			new PropertyMetadata<Border, CornerRadius> { PropertyChanged = static (b, _, _) => b.InvalidateContent() });

	/// <summary>The width of the border on each side. SUIsei strokes all four sides at the left side's width.</summary>
	public static readonly DependencyProperty<Border, Thickness> BorderThicknessProperty =
		DependencyProperty.Register(nameof(BorderThickness),
			new PropertyMetadata<Border, Thickness> { Invalidates = InvalidationFlags.Measure, Field = static b => ref b._borderThickness });

	/// <summary>The space between the border and the child.</summary>
	public static readonly DependencyProperty<Border, Thickness> PaddingProperty =
		DependencyProperty.Register(nameof(Padding),
			new PropertyMetadata<Border, Thickness> { Invalidates = InvalidationFlags.Measure, Field = static b => ref b._padding });

	// BorderThickness and Padding, kept by the property store (PropertyMetadata.Field) so layout reads fields.
	private Thickness _borderThickness, _padding;

	// VisualChildren, rebuilt only when Child changes, so layout and hit-testing read it without allocating.
	private UIElement[] _visualChildren = [];
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
			_visualChildren = value is null ? [] : [value];
			if (value is not null)
				AdoptChild(value);
			InvalidateMeasure();
		}
	}

	/// <summary>The brush painting the area inside the border; null paints nothing there.</summary>
	public Brush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

	/// <summary>The brush painting the border; null draws none.</summary>
	public Brush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

	/// <summary>How a change of <see cref="Background"/> animates; null shows it at once.</summary>
	public BrushTransition? BackgroundTransition { get; set; }

	public Thickness BorderThickness { get => _borderThickness; set => SetValue(BorderThicknessProperty, value); }
	public Thickness Padding { get => _padding; set => SetValue(PaddingProperty, value); }

	/// <summary>The radius of each corner.</summary>
	public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

	internal override IReadOnlyList<UIElement> VisualChildren => _visualChildren;

	// BorderThickness plus Padding, from their fields.
	private Thickness Inset => new(
		_borderThickness.Left + _padding.Left, _borderThickness.Top + _padding.Top,
		_borderThickness.Right + _padding.Right, _borderThickness.Bottom + _padding.Bottom);

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

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Background, attached);
		Listen(BorderBrush, attached);
		base.OnLiveTreeChanged(attached);
	}

	private protected override VisualContent? UpdateContent()
	{
		var fill = BackgroundPaint(Background);
		var border = BorderBrush?.CompositionBrush;
		var thickness = BorderThickness;
		if (fill is null && (border is null || thickness.Horizontal + thickness.Vertical <= 0f))
			return null;
		return RetainedRect(fill, CornerRadius.ToComposition(), border, thickness.Left);
	}
}
