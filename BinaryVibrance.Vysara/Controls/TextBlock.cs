using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Documents;
using BinaryVibrance.Vysara.Extensions;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Controls;

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
/// breaks into lines at its line breaks and, with <see cref="TextWrapping"/>, at spaces to keep inside the width it is
/// measured at; <see cref="TextTrimming"/> cuts a line still too wide short with an ellipsis; and
/// <see cref="TextAlignment"/> places each line across the width the text block is arranged at.
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
	/// the text block's layout box. Null paints white, Vysara's default text colour.</summary>
	public static readonly DependencyProperty<TextBlock, Brush?> ForegroundProperty =
		DependencyProperty.Register(nameof(Foreground),
			new PropertyMetadata<TextBlock, Brush?>
			{
				Inherits = InheritedText.Foreground,
				Validate = static (_, brush) => InheritedText.ValidateForeground(brush),
				PropertyChanged = static (t, old, @new) => t.OnBrushChanged(old, @new),
			});

	/// <summary>How each line sits across the width the text block is arranged at: WinUI's <c>TextAlignment</c>,
	/// <see cref="BinaryVibrance.Vysara.TextAlignment.Left"/> by default. A change repaints in place, with no layout.</summary>
	public static readonly DependencyProperty<TextBlock, TextAlignment> TextAlignmentProperty =
		DependencyProperty.Register(nameof(TextAlignment),
			new PropertyMetadata<TextBlock, TextAlignment>
			{
				DefaultValue = TextAlignment.Left,
				PropertyChanged = static (t, _, _) => t.InvalidateContent(),
			});

	/// <summary>Whether lines also break at spaces to keep inside the width: WinUI's <c>TextWrapping</c>,
	/// <see cref="BinaryVibrance.Vysara.TextWrapping.NoWrap"/> by default.</summary>
	public static readonly DependencyProperty<TextBlock, TextWrapping> TextWrappingProperty =
		DependencyProperty.Register<TextBlock, TextWrapping>(nameof(TextWrapping),
			new PropertyMetadata<TextBlock, TextWrapping> { DefaultValue = TextWrapping.NoWrap, Invalidates = InvalidationFlags.Measure });

	/// <summary>Whether a line still wider than the width is cut short with an ellipsis: WinUI's <c>TextTrimming</c>,
	/// <see cref="BinaryVibrance.Vysara.TextTrimming.None"/> by default.</summary>
	public static readonly DependencyProperty<TextBlock, TextTrimming> TextTrimmingProperty =
		DependencyProperty.Register<TextBlock, TextTrimming>(nameof(TextTrimming),
			new PropertyMetadata<TextBlock, TextTrimming> { DefaultValue = TextTrimming.None, Invalidates = InvalidationFlags.Measure });

	// The paint of text with no Foreground. Shared by every such run and never changed.
	private static readonly CompositionColorBrush DefaultForeground = new(HdrColor.FromRgb(255, 255, 255));

	/// <summary>The text to lay out and render as MTSDF glyphs: a line per line break, and more where it wraps.</summary>
	public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

	/// <summary>Font size in DIPs: 14 unless set here or inherited from above.</summary>
	public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

	/// <summary>The family of the text's font, by name in the host's <see cref="UIHost.Fonts"/>, inherited from above
	/// unless set here; null, or a name the host doesn't hold, draws in the root's <see cref="UIRoot.Font"/>.</summary>
	public static readonly DependencyProperty<TextBlock, FontFamily?> FontFamilyProperty =
		DependencyProperty.Register(nameof(FontFamily),
			new PropertyMetadata<TextBlock, FontFamily?> { Invalidates = InvalidationFlags.Measure, Inherits = InheritedText.FontFamily });

	/// <summary>The family of the text's font (<see cref="FontFamilyProperty"/>).</summary>
	public FontFamily? FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

	private protected override FontFamily? TextFamily => FontFamily;

	/// <summary>The brush the glyphs are painted with; null paints white. A change repaints with no relayout.</summary>
	public Brush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

	/// <summary>How each line sits across the width the text block is arranged at.</summary>
	public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }

	/// <summary>Whether lines also break at spaces to keep inside the width the text block is measured at.</summary>
	public TextWrapping TextWrapping { get => GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }

	/// <summary>Whether a line still wider than that width is cut short with an ellipsis.</summary>
	public TextTrimming TextTrimming { get => GetValue(TextTrimmingProperty); set => SetValue(TextTrimmingProperty, value); }

	// Measured from the run's shaping: each line's width in EM, which a size doesn't change, times the size. So an animated
	// FontSize, or one inherited from above, re-measures without shaping anything (unless the text is fitted to the width,
	// which is in EM too), and the same shaping draws the run.
	protected override Size MeasureOverride(Size available)
	{
		var font = TextFont;
		_fit = TextFit.For(available.Width, FontSize, TextWrapping, TextTrimming);
		var shaped = Shaped(Text, font, Typography.FeaturesOf(this));
		return new Size(MathF.Min(shaped.Width * FontSize, available.Width), shaped.Lines.Count * font.LineHeight * FontSize);
	}

	// The text shaped, kept across measures and handed to the run this element draws, so measuring and drawing shape once
	// between them: shaped again only when the text, the font, its shaper or the features change.
	private ShapedGlyphRun? _shaped;
	private TextFit _fit;

	private ShapedGlyphRun Shaped(string text, FontAtlas font, FontFeatureSet features)
	{
		var shaped = _shaped ??= new ShapedGlyphRun();
		if (!shaped.IsShapingOf(text, font, features, _fit))
			shaped.Reshape(text, font, features, _fit);
		return shaped;
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
		content.TextAlignment = TextAlignment.ToComposition();
		content.Fit = _fit;
		content.UpdateEffects(TextEffects.GetOutlineThickness(this), TextEffects.GetOutlineBrush(this)?.CompositionBrush,
			TextEffects.GetShadowOffset(this).ToVector2(), TextEffects.GetShadowSoftness(this), TextEffects.GetShadowBrush(this)?.CompositionBrush);
		// The run draws from the shaping its measure made, rather than shaping the same text again.
		if (_shaped is { } shaped && shaped.IsShapingOf(text, content.Font, content.Features, _fit))
			content.SetDerived(shaped);
		return content;
	}

	/// <summary>A <see cref="TextEffects"/> size or offset changed: repaint in place, with no layout or shaping.</summary>
	internal void OnTextEffectChanged() => InvalidateContent();

	/// <summary>A <see cref="TextEffects"/> brush changed: move the listener and repaint in place.</summary>
	internal void OnTextEffectBrushChanged(Brush? old, Brush? @new) => OnBrushChanged(old, @new);
}
