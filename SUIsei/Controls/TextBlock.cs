using System.Numerics;
using SUIsei.Composition;
using SUIsei.Documents;
using SUIsei.Extensions;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A run of text. Measures and emits glyphs against its root's font (<see cref="UIRoot.Font"/>, the embedded
/// <see cref="FontAtlas.Default"/> unless the host sets one) through that atlas's shaper, with the OpenType features
/// its <see cref="Documents.Typography"/> settings name, and hands the same font and features to the
/// <see cref="GlyphRunContent"/> the compositor lays out into <see cref="UIPrimKind.Glyph"/> draws, so the run draws in
/// the width layout measured.
/// <para>
/// <see cref="FontSize"/> and <see cref="Foreground"/> are inherited, as in WinUI: unless set here (locally, by a style or
/// by an animation), they are those of the nearest <see cref="ContentPresenter"/>, <see cref="Control"/> or text block
/// above, so the text a control's template or its string content shows takes the control's size and brush. The text
/// breaks into lines at its line breaks, with no wrapping, and <see cref="TextAlignment"/> places each line across the
/// width the text block is arranged at.
/// </para>
/// </summary>
public sealed class TextBlock : FrameworkElement
{
	public static readonly DependencyProperty<TextBlock, string> TextProperty =
		DependencyProperty.Register<TextBlock, string>(nameof(Text),
			new PropertyMetadata<TextBlock, string> { DefaultValue = "", Invalidates = InvalidationFlags.Measure });

	/// <summary>The size of the text in DIPs, inherited from above unless set here.</summary>
	public static readonly DependencyProperty<TextBlock, float> FontSizeProperty =
		DependencyProperty.Register<TextBlock, float>(nameof(FontSize),
			new PropertyMetadata<TextBlock, float> { DefaultValue = 14f, Invalidates = InvalidationFlags.Measure, Inherits = InheritedText.FontSize });

	/// <summary>The brush the glyphs are painted with, inherited from above unless set here; a gradient is measured over
	/// the text block's layout box. Null paints white, SUIsei's default text colour.</summary>
	public static readonly DependencyProperty<TextBlock, Brush?> ForegroundProperty =
		DependencyProperty.Register(nameof(Foreground),
			new PropertyMetadata<TextBlock, Brush?>
			{
				Inherits = InheritedText.Foreground,
				Validate = static (_, brush) => InheritedText.ValidateForeground(brush),
				PropertyChanged = static (t, old, @new) => t.OnBrushChanged(old, @new),
			});

	/// <summary>How each line sits across the width the text block is arranged at: WinUI's <c>TextAlignment</c>,
	/// <see cref="SUIsei.TextAlignment.Left"/> by default. A change repaints in place, with no layout.</summary>
	public static readonly DependencyProperty<TextBlock, TextAlignment> TextAlignmentProperty =
		DependencyProperty.Register(nameof(TextAlignment),
			new PropertyMetadata<TextBlock, TextAlignment>
			{
				DefaultValue = TextAlignment.Left,
				PropertyChanged = static (t, _, _) => t.InvalidateContent(),
			});

	// The paint of text with no Foreground. Shared by every such run and never changed.
	private static readonly CompositionColorBrush DefaultForeground = new(HdrColor.FromRgb(255, 255, 255));

	/// <summary>The text to lay out and render as MTSDF glyphs: a line per line break, with no wrapping.</summary>
	public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

	/// <summary>Font size in DIPs: 14 unless set here or inherited from above.</summary>
	public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

	/// <summary>The brush the glyphs are painted with; null paints white. A change repaints with no relayout.</summary>
	public Brush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

	/// <summary>How each line sits across the width the text block is arranged at.</summary>
	public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }

	// As wide as the widest line, and a line height per line.
	protected override Size MeasureOverride(Size available)
	{
		var font = TextFont;
		var fontSize = FontSize;
		var features = Typography.FeaturesOf(this);
		var text = Text.AsSpan();
		var widest = 0f;
		var lines = 0;
		for (var start = 0; start >= 0; lines++)
		{
			var line = text.Slice(start, TextLines.Next(text, start, out var next));
			if (!line.IsEmpty)
				widest = MathF.Max(widest, font.MeasureWidth(line, features));
			start = next;
		}
		return new Size(MathF.Min(widest * fontSize, available.Width), lines * font.LineHeight * fontSize);
	}

	// The run this element draws, kept across updates so a recolour or re-arrange allocates nothing.
	private GlyphRunContent? _glyphRun;

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Foreground, attached);
		Listen(TextEffects.GetOutlineBrush(this), attached);
		Listen(TextEffects.GetShadowBrush(this), attached);
		// A root brings its font: one moved between roots measures again with the new root's.
		if (attached)
			InvalidateMeasure();
		base.OnLiveTreeChanged(attached);
	}

	private protected override VisualContent? UpdateContent()
	{
		var text = Text;
		if (string.IsNullOrEmpty(text))
			return null;
		var content = _glyphRun ??= new GlyphRunContent();
		content.Update(text, FontSize, Foreground?.CompositionBrush ?? DefaultForeground, new Vector2(LayoutRect.Width, LayoutRect.Height),
			TextFont, Typography.FeaturesOf(this));
		content.TextAlignment = TextAlignment;
		content.UpdateEffects(TextEffects.GetOutlineThickness(this), TextEffects.GetOutlineBrush(this)?.CompositionBrush,
			TextEffects.GetShadowOffset(this).ToVector2(), TextEffects.GetShadowSoftness(this), TextEffects.GetShadowBrush(this)?.CompositionBrush);
		return content;
	}

	/// <summary>A <see cref="TextEffects"/> size or offset changed: repaint in place, with no layout or shaping.</summary>
	internal void OnTextEffectChanged() => InvalidateContent();

	/// <summary>A <see cref="TextEffects"/> brush changed: move the listener and repaint in place.</summary>
	internal void OnTextEffectBrushChanged(Brush? old, Brush? @new) => OnBrushChanged(old, @new);
}
