namespace SUIsei.Controls;

/// <summary>
/// A run of text. Measures and emits glyphs against the engine's default MTSDF font
/// (<see cref="FontAtlas.Default"/>): an advance-based width (no HarfBuzz shaping) and a
/// <see cref="GlyphRunContent"/> the compositor lays out into <see cref="Glyph"/> draws.
/// </summary>
public sealed class TextBlock : FrameworkElement
{
	public static readonly DependencyProperty<TextBlock, string> TextProperty =
		DependencyProperty.Register<TextBlock, string>(nameof(Text),
			new PropertyMetadata<TextBlock, string> { DefaultValue = "", Invalidates = InvalidationFlags.Measure });

	public static readonly DependencyProperty<TextBlock, float> FontSizeProperty =
		DependencyProperty.Register<TextBlock, float>(nameof(FontSize),
			new PropertyMetadata<TextBlock, float> { DefaultValue = 14f, Invalidates = InvalidationFlags.Measure });

	/// <summary>The text to lay out and render as MTSDF glyphs (advance-based width; no HarfBuzz shaping).</summary>
	public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

	/// <summary>Font size in DIPs.</summary>
	public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

	/// <summary>Glyph colour. A colour animation on <c>Foreground</c> overrides it on the visual path, with no relayout.</summary>
	public HdrColor Foreground { get; set { if (field != value) { field = value; InvalidateContent(); } } } = HdrColor.FromRgb(255, 255, 255);

	protected override Size MeasureOverride(Size available)
	{
		var font = FontAtlas.Default;
		var height = font.LineHeight * FontSize;
		var text = Text;
		if (string.IsNullOrEmpty(text))
			return new Size(0f, height);
		var width = MathF.Min(font.MeasureWidth(text) * FontSize, available.Width);
		return new Size(width, height);
	}

	// The run this element draws, kept across updates so a recolour or re-arrange allocates nothing.
	private GlyphRunContent? _glyphRun;

	private protected override VisualContent? UpdateContent()
	{
		var text = Text;
		if (string.IsNullOrEmpty(text))
			return null;
		var content = _glyphRun ??= new GlyphRunContent();
		content.Update(text, FontSize, AnimatedFill ?? Foreground);
		return content;
	}
}
