namespace BinaryVibrance.Vysara;

/// <summary>
/// Adds the WinUI framework-level layout inputs on top of <see cref="UIElement"/>:
/// explicit <see cref="Width"/>/<see cref="Height"/> (NaN = Auto), <see cref="MinWidth"/>/<see cref="MaxWidth"/>
/// (and the height pair), <see cref="Margin"/>, and alignment. It implements the WinUI
/// <c>MeasureCore</c>/<c>ArrangeCore</c> wrapper — deflate by margin, clamp to min/max/explicit, call the
/// element's <c>MeasureOverride</c>/<c>ArrangeOverride</c>, reinflate — so every control only writes its
/// intrinsic sizing.
/// <para>
/// The layout inputs are dependency properties, as in WinUI, so a <see cref="Style"/> sets them and a storyboard
/// animates the sizes. Layout reads them on every pass, so they are field-backed, as WinUI's core keeps them: each one's
/// value is in a field here, which measure and arrange read, and an element whose layout only code or markup set holds
/// nothing for them in the property store.
/// </para>
/// </summary>
public abstract class FrameworkElement : UIElement
{
	/// <summary>The element's width in DIPs. NaN, the default, is Auto: the width comes from the content and the slot.
	/// XAML writes Auto as <c>Width="Auto"</c>.</summary>
	public static readonly DependencyProperty<FrameworkElement, float> WidthProperty =
		DependencyProperty.Register(nameof(Width),
			new PropertyMetadata<FrameworkElement, float>
			{
				DefaultValue = float.NaN,
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._width,
			});

	/// <summary>The element's height in DIPs. NaN, the default, is Auto, as for <see cref="WidthProperty"/>.</summary>
	public static readonly DependencyProperty<FrameworkElement, float> HeightProperty =
		DependencyProperty.Register(nameof(Height),
			new PropertyMetadata<FrameworkElement, float>
			{
				DefaultValue = float.NaN,
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._height,
			});

	public static readonly DependencyProperty<FrameworkElement, float> MinWidthProperty =
		DependencyProperty.Register(nameof(MinWidth),
			new PropertyMetadata<FrameworkElement, float>
			{
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._minWidth,
			});

	public static readonly DependencyProperty<FrameworkElement, float> MinHeightProperty =
		DependencyProperty.Register(nameof(MinHeight),
			new PropertyMetadata<FrameworkElement, float>
			{
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._minHeight,
			});

	public static readonly DependencyProperty<FrameworkElement, float> MaxWidthProperty =
		DependencyProperty.Register(nameof(MaxWidth),
			new PropertyMetadata<FrameworkElement, float>
			{
				DefaultValue = float.PositiveInfinity,
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._maxWidth,
			});

	public static readonly DependencyProperty<FrameworkElement, float> MaxHeightProperty =
		DependencyProperty.Register(nameof(MaxHeight),
			new PropertyMetadata<FrameworkElement, float>
			{
				DefaultValue = float.PositiveInfinity,
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._maxHeight,
			});

	public static readonly DependencyProperty<FrameworkElement, Thickness> MarginProperty =
		DependencyProperty.Register(nameof(Margin),
			new PropertyMetadata<FrameworkElement, Thickness>
			{
				Invalidates = InvalidationFlags.Measure,
				Field = static e => ref e._margin,
			});

	public static readonly DependencyProperty<FrameworkElement, HorizontalAlignment> HorizontalAlignmentProperty =
		DependencyProperty.Register(nameof(HorizontalAlignment),
			new PropertyMetadata<FrameworkElement, HorizontalAlignment>
			{
				DefaultValue = HorizontalAlignment.Stretch,
				Invalidates = InvalidationFlags.Arrange,
				Field = static e => ref e._horizontalAlignment,
			});

	public static readonly DependencyProperty<FrameworkElement, VerticalAlignment> VerticalAlignmentProperty =
		DependencyProperty.Register(nameof(VerticalAlignment),
			new PropertyMetadata<FrameworkElement, VerticalAlignment>
			{
				DefaultValue = VerticalAlignment.Stretch,
				Invalidates = InvalidationFlags.Arrange,
				Field = static e => ref e._verticalAlignment,
			});

	/// <summary>An object of the app's own kept with the element, as WinUI's <c>Tag</c>. Vysara never reads it.</summary>
	public static readonly DependencyProperty<FrameworkElement, object?> TagProperty =
		DependencyProperty.Register(nameof(Tag), new PropertyMetadata<FrameworkElement, object?>());

	// The layout properties' values, kept by the property store (PropertyMetadata.Field); each starts at its default.
	private float _width = float.NaN, _height = float.NaN, _minWidth, _minHeight;
	private float _maxWidth = float.PositiveInfinity, _maxHeight = float.PositiveInfinity;
	private Thickness _margin;
	private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
	private VerticalAlignment _verticalAlignment = VerticalAlignment.Stretch;

	public float Width { get => _width; set => SetValue(WidthProperty, value); }
	public float Height { get => _height; set => SetValue(HeightProperty, value); }
	public float MinWidth { get => _minWidth; set => SetValue(MinWidthProperty, value); }
	public float MinHeight { get => _minHeight; set => SetValue(MinHeightProperty, value); }
	public float MaxWidth { get => _maxWidth; set => SetValue(MaxWidthProperty, value); }
	public float MaxHeight { get => _maxHeight; set => SetValue(MaxHeightProperty, value); }
	public Thickness Margin { get => _margin; set => SetValue(MarginProperty, value); }
	public HorizontalAlignment HorizontalAlignment { get => _horizontalAlignment; set => SetValue(HorizontalAlignmentProperty, value); }
	public VerticalAlignment VerticalAlignment { get => _verticalAlignment; set => SetValue(VerticalAlignmentProperty, value); }

	/// <summary>An object of the app's own kept with the element, such as what a shared click handler needs to tell its
	/// senders apart. Vysara never reads it.</summary>
	public object? Tag { get => GetValue(TagProperty); set => SetValue(TagProperty, value); }

	/// <summary>Optional name (the <c>x:Name</c> the generator assigns; diagnostics only).</summary>
	public string? Name { get; set; }

	public static readonly DependencyProperty<FrameworkElement, Style?> StyleProperty =
		DependencyProperty.Register(nameof(Style),
			new PropertyMetadata<FrameworkElement, Style?>
			{
				// Every setter is checked before the style is stored, so one the element refuses leaves it with its old style.
				Validate = static (o, style) => style?.Validate(o),
				PropertyChanged = static (o, old, @new) => { old?.Clear(o); @new?.Apply(o); }
			});

	/// <summary>The style applied to this element. Writing clears the previous style's setters before applying the
	/// new one (Style rung; below Local, above Default).</summary>
	public Style? Style { get => GetValue(StyleProperty); set => SetValue(StyleProperty, value); }

	/// <summary>Per-element resource dictionary (the lookup root for <c>{StaticResource}</c> at runtime).</summary>
	public ResourceDictionary? Resources { get; set; }

	/// <summary>Walk the element tree from this element to the root, returning the first resource with
	/// <paramref name="key"/>, or null. Used by the generated <c>InitializeComponent</c> for
	/// <c>{StaticResource key}</c> references. An element shown over the page for another (a tooltip in the overlay layer)
	/// goes on from that element's tree once its own runs out, as WinUI's tooltip finds its owner's resources.</summary>
	public object? FindResource(string key)
	{
		for (UIElement? el = this; el is not null; el = el.Parent ?? (el as FrameworkElement)?.ResourceOwner)
		{
			if (el is FrameworkElement fe && fe.Resources?.TryFind(key, out var v) == true)
				return v;
		}
		return null;
	}

	/// <summary>The element whose resources this one goes on to find where its own tree has none: the element a tooltip
	/// belongs to.</summary>
	internal UIElement? ResourceOwner { get; set; }

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

		// On whole pixels where the root asks (UIRoot.UseLayoutRounding): both edges rounded, so neighbours still meet
		if (AttachedRoot is { UseLayoutRounding: true, LayoutScale: > 0f and var scale })
		{
			(x, w) = RoundEdges(x, w, scale);
			(y, h) = RoundEdges(y, h, scale);
		}

		LayoutRect = new Rect(x, y, w, h);
		ArrangeOverride(new Size(w, h));
	}

	// A span from `start`, `size` long, with both its edges moved to the nearest whole pixel at `scale` pixels per DIP
	private static (float Start, float Size) RoundEdges(float start, float size, float scale)
	{
		var from = MathF.Round(start * scale) / scale;
		var to = MathF.Round((start + size) * scale) / scale;
		return (from, MathF.Max(0f, to - from));
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

	// The live x:Binds of the data template that built this element, attached while it is in a live tree.
	private Controls.IDataTemplateBindings? _templateBindings;

	/// <summary>Keeps <paramref name="bindings"/> with this element (<see cref="Controls.DataTemplate.SetBindings"/>),
	/// attaching them now if it is already in a live tree.</summary>
	internal void SetTemplateBindings(Controls.IDataTemplateBindings bindings)
	{
		_templateBindings?.Detach();
		_templateBindings = bindings;
		if (AttachedRoot is not null)
			bindings.Attach();
	}

	private protected override void OnLiveTreeChanged(bool attached)
	{
		base.OnLiveTreeChanged(attached);
		if (_templateBindings is null)
			return;
		if (attached)
			_templateBindings.Attach();
		else
			_templateBindings.Detach();
	}
}
