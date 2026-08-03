namespace SUIsei;

/// <summary>
/// Adds the WinUI framework-level layout inputs on top of <see cref="UIElement"/>:
/// explicit <see cref="Width"/>/<see cref="Height"/> (NaN = Auto), <see cref="MinWidth"/>/<see cref="MaxWidth"/>
/// (and the height pair), <see cref="Margin"/>, and alignment. It implements the WinUI
/// <c>MeasureCore</c>/<c>ArrangeCore</c> wrapper — deflate by margin, clamp to min/max/explicit, call the
/// element's <c>MeasureOverride</c>/<c>ArrangeOverride</c>, reinflate — so every control only writes its
/// intrinsic sizing. Backing storage uses the C# 14 <c>field</c> keyword (no hand-declared fields).
/// </summary>
public abstract class FrameworkElement : UIElement
{
	public float Width { get; set { if (!Same(field, value)) { field = value; InvalidateMeasure(); } } } = float.NaN;
	public float Height { get; set { if (!Same(field, value)) { field = value; InvalidateMeasure(); } } } = float.NaN;
	public float MinWidth { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }
	public float MinHeight { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }
	public float MaxWidth { get; set { if (field != value) { field = value; InvalidateMeasure(); } } } = float.PositiveInfinity;
	public float MaxHeight { get; set { if (field != value) { field = value; InvalidateMeasure(); } } } = float.PositiveInfinity;
	public Thickness Margin { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }
	public HorizontalAlignment HorizontalAlignment { get; set { if (field != value) { field = value; InvalidateArrange(); } } } = HorizontalAlignment.Stretch;
	public VerticalAlignment VerticalAlignment { get; set { if (field != value) { field = value; InvalidateArrange(); } } } = VerticalAlignment.Stretch;

	/// <summary>Optional name (the <c>x:Name</c> the generator assigns; diagnostics only).</summary>
	public string? Name { get; set; }

	public static readonly DependencyProperty<FrameworkElement, Style?> StyleProperty =
		DependencyProperty.Register(nameof(Style),
			new PropertyMetadata<FrameworkElement, Style?>
			{
				PropertyChanged = static (o, old, @new) => { old?.Clear(o); @new?.Apply(o); }
			});

	/// <summary>The style applied to this element. Writing clears the previous style's setters before applying the
	/// new one (Style rung; below Local, above Default).</summary>
	public Style? Style { get => GetValue(StyleProperty); set => SetValue(StyleProperty, value); }

	/// <summary>Per-element resource dictionary (the lookup root for <c>{StaticResource}</c> at runtime).</summary>
	public ResourceDictionary? Resources { get; set; }

	/// <summary>Walk the element tree from this element to the root, returning the first resource with
	/// <paramref name="key"/>, or null. Used by the generated <c>InitializeComponent</c> for
	/// <c>{StaticResource key}</c> references.</summary>
	public object? FindResource(string key)
	{
		for (UIElement? el = this; el is not null; el = el.Parent)
		{
			if (el is FrameworkElement fe && fe.Resources?.TryFind(key, out var v) == true)
				return v;
		}
		return null;
	}

	// NaN-safe equality so re-assigning Auto (NaN) doesn't read as a change.
	private static bool Same(float a, float b) => a.Equals(b);

	protected override Size MeasureCore(Size availableSize)
	{
		var m = Margin;
		var availW = MathF.Max(0f, availableSize.Width - m.Horizontal);
		var availH = MathF.Max(0f, availableSize.Height - m.Vertical);

		// What the element gets to measure against: explicit size if set, else the (margin-deflated) available,
		// each clamped to its min/max.
		var cw = Clamp(float.IsNaN(Width) ? availW : Width, MinWidth, MaxWidth);
		var ch = Clamp(float.IsNaN(Height) ? availH : Height, MinHeight, MaxHeight);

		var desired = MeasureOverride(new Size(cw, ch));

		// Explicit size wins over intrinsic; then clamp to min/max again.
		var dw = Clamp(float.IsNaN(Width) ? desired.Width : Width, MinWidth, MaxWidth);
		var dh = Clamp(float.IsNaN(Height) ? desired.Height : Height, MinHeight, MaxHeight);

		// Reinflate by margin, and never report larger than offered.
		return new Size(
			MathF.Min(dw + m.Horizontal, availableSize.Width),
			MathF.Min(dh + m.Vertical, availableSize.Height));
	}

	protected override void ArrangeCore(Rect finalRect)
	{
		var m = Margin;
		var slotX = finalRect.X + m.Left;
		var slotY = finalRect.Y + m.Top;
		var slotW = MathF.Max(0f, finalRect.Width - m.Horizontal);
		var slotH = MathF.Max(0f, finalRect.Height - m.Vertical);

		var (x, w) = ArrangeAxis((int)HorizontalAlignment, slotX, slotW,
			MathF.Max(0f, DesiredSize.Width - m.Horizontal), Width, MinWidth, MaxWidth);
		var (y, h) = ArrangeAxis((int)VerticalAlignment, slotY, slotH,
			MathF.Max(0f, DesiredSize.Height - m.Vertical), Height, MinHeight, MaxHeight);

		LayoutRect = new Rect(x, y, w, h);
		ArrangeOverride(new Size(w, h));
	}

	// Resolve one axis: pick the content extent (Stretch fills the slot; else explicit-or-desired, clamped) and
	// position it within the slot by alignment. alignmentIndex: 0=near, 1=center, 2=far, 3=stretch.
	private static (float pos, float size) ArrangeAxis(int alignmentIndex, float slotStart, float slotSize, float desired, float explicitSize, float min, float max)
	{
		var size = alignmentIndex == 3 && float.IsNaN(explicitSize)
			? slotSize
			: Clamp(float.IsNaN(explicitSize) ? desired : explicitSize, min, max);
		size = MathF.Min(size, slotSize);
		var pos = alignmentIndex switch
		{
			2 => slotStart + (slotSize - size),           // Far (Right / Bottom)
														  // Center; and Stretch that an explicit size kept from filling its slot also centers (WinUI: Stretch
														  // degrades to Center when the element can't fill). A true stretch fills the slot, so the offset is 0.
			1 or 3 => slotStart + (slotSize - size) / 2f,
			_ => slotStart,                               // Near (Left / Top)
		};
		return (pos, size);
	}

	private static float Clamp(float v, float min, float max) => MathF.Min(MathF.Max(v, min), max);
}
