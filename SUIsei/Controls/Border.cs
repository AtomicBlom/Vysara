using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A single-child decorator with a background, a border, padding, and rounded corners:
/// measure deflates by border + padding around the child; arrange insets the child by the same; the visual draws a
/// (rounded) fill + border. <see cref="BorderThickness"/> is treated as uniform for the drawn stroke, and
/// <see cref="CornerRadius"/> must be uniform.
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

	/// <summary>The corner radius. SUIsei draws one radius for all four corners: radii that differ throw
	/// <see cref="NotSupportedException"/> when set.</summary>
	public static readonly DependencyProperty<Border, CornerRadius> CornerRadiusProperty =
		DependencyProperty.Register(nameof(CornerRadius),
			new PropertyMetadata<Border, CornerRadius>
			{
				PropertyChanged = static (b, old, value) =>
				{
					_ = value.Uniform; // fail loud now, not at the next draw
					b.InvalidateContent();
				},
			});

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

	public Thickness BorderThickness { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }
	public Thickness Padding { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }

	/// <summary>The radius of the corners, uniform.</summary>
	public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

	internal override IReadOnlyList<UIElement> VisualChildren => _visualChildren;

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
		if (fill is null && (border is null || BorderThickness.Horizontal + BorderThickness.Vertical <= 0f))
			return null;
		return RetainedRect(fill, CornerRadius.Uniform, border, BorderThickness.Left);
	}
}
