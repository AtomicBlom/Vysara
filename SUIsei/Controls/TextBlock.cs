using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A run of text. Measures and emits glyphs against the engine's default MTSDF font
/// (<see cref="FontAtlas.Default"/>): an advance-based width (no HarfBuzz shaping) and a
/// <see cref="GlyphRunContent"/> the compositor lays out into <see cref="UIPrimKind.Glyph"/> draws.
/// </summary>
public sealed class TextBlock : FrameworkElement
{
	public static readonly DependencyProperty<TextBlock, string> TextProperty =
		DependencyProperty.Register<TextBlock, string>(nameof(Text),
			new PropertyMetadata<TextBlock, string> { DefaultValue = "", Invalidates = InvalidationFlags.Measure });

	public static readonly DependencyProperty<TextBlock, float> FontSizeProperty =
		DependencyProperty.Register<TextBlock, float>(nameof(FontSize),
			new PropertyMetadata<TextBlock, float> { DefaultValue = 14f, Invalidates = InvalidationFlags.Measure });

	/// <summary>The brush the glyphs are painted with; a gradient is measured over the text block's layout box. Null
	/// paints white, SUIsei's default text colour.</summary>
	public static readonly DependencyProperty<TextBlock, Brush?> ForegroundProperty =
		DependencyProperty.Register(nameof(Foreground),
			new PropertyMetadata<TextBlock, Brush?> { PropertyChanged = static (t, old, @new) => t.OnBrushChanged(old, @new) });

	// The paint of text with no Foreground. Shared by every such run and never changed.
	private static readonly CompositionColorBrush DefaultForeground = new(HdrColor.FromRgb(255, 255, 255));

	/// <summary>The text to lay out and render as MTSDF glyphs (advance-based width; no HarfBuzz shaping).</summary>
	public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

	/// <summary>Font size in DIPs.</summary>
	public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

	/// <summary>The brush the glyphs are painted with; null paints white. A change repaints with no relayout.</summary>
	public Brush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

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

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Foreground, attached);
		base.OnLiveTreeChanged(attached);
	}

	private protected override VisualContent? UpdateContent()
	{
		var text = Text;
		if (string.IsNullOrEmpty(text))
			return null;
		var content = _glyphRun ??= new GlyphRunContent();
		content.Update(text, FontSize, Foreground?.CompositionBrush ?? DefaultForeground, new Vector2(LayoutRect.Width, LayoutRect.Height));
		return content;
	}
}
