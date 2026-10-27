using System.Globalization;
using SUIsei.Controls;
using SUIsei.Media;

namespace SUIsei.Extensions;

/// <summary>
/// An outline and a shadow on text, set in XAML on a <see cref="TextBlock"/> as
/// <c>suisei:TextEffects.OutlineThickness="0.08" suisei:TextEffects.OutlineBrush="Black"</c>, and
/// <c>ShadowOffset</c>, <c>ShadowSoftness</c> and <c>ShadowBrush</c>. WinUI has neither for text. Both are drawn in the
/// text's own draw from the font atlas's true-distance channel, and sized in EM, so they scale with the font size.
/// <para>
/// The outline is the glyphs grown by its thickness. The shadow is the glyphs moved by its offset, their edge fading
/// across its softness, centred on the moved edge; it is cast by the glyphs, not their outline. Every glyph's shadow
/// lies under every glyph's outline, and every outline under every glyph, so an opaque effect is exact: no glyph's
/// effect covers a neighbouring glyph. A translucent effect blends twice where neighbouring glyphs' effects overlap
/// (outlines meet between letters, soft halos run into each other), a gap; for a soft translucent shadow the fallback is
/// S-37's layer drop shadow. The outline lies under the whole glyph rather than round it, so a translucent
/// <see cref="TextBlock.Foreground"/> shows it through. Fading the element with <c>Opacity</c> fades the text and its
/// effects as one.
/// </para>
/// <para>
/// The atlas holds distances <see cref="FontAtlas.DistanceRangeEm"/> / 2 past each edge, so an outline can be at most
/// that thick and a shadow at most <see cref="FontAtlas.DistanceRangeEm"/> soft, as it fades half its softness past the
/// edge. A size beyond its font's field throws where it enters: when it is set on text in a tree, whose font is known,
/// and for text set up outside one, when it becomes a root's <c>Content</c> or overlay; setting a root's
/// <see cref="UIRoot.Font"/> checks the text it holds first. Each throws before anything changes. An animation's own
/// values (its start and end, its keyframes) are checked as it begins, never mid-animation. A negative or non-finite
/// size throws whenever it is set. Text added to a panel or template already in a tree is not checked as it joins
/// (adding it cannot be undone part-way), a gap: like an easing's overshoot, an effect the field cannot hold is drawn
/// clamped to it.
/// </para>
/// <para>
/// The effects take a colour or a linear gradient, measured over the text's layout box (moved with the shadow, for the
/// shadow); an image brush throws when set. Animating a size, the offset or a brush costs no layout and no shaping, and
/// allocates nothing. Set on anything but a <see cref="TextBlock"/> they are SUISEI004, as SUIsei has no inherited text
/// properties yet.
/// </para>
/// </summary>
public sealed class TextEffects : DependencyObject
{
	private TextEffects()
	{
	}

	/// <summary>How far the outline reaches past the glyphs' edges, in EM. 0 by default: no outline.</summary>
	public static readonly DependencyProperty<TextBlock, float> OutlineThicknessProperty =
		DependencyProperty.RegisterAttached("OutlineThickness",
			new PropertyMetadata<TextBlock, float> { Validate = static (text, value) => ValidateOutline(text, value, text.AttachedRoot?.Font), PropertyChanged = static (text, _, _) => text.OnTextEffectChanged() });

	/// <summary>The outline's paint, a colour or a linear gradient over the text's layout box. Null by default: no
	/// outline.</summary>
	public static readonly DependencyProperty<TextBlock, Brush?> OutlineBrushProperty =
		DependencyProperty.RegisterAttached("OutlineBrush",
			new PropertyMetadata<TextBlock, Brush?> { Validate = static (_, brush) => ValidateBrush(brush), PropertyChanged = static (text, old, @new) => text.OnTextEffectBrushChanged(old, @new) });

	/// <summary>Where the shadow lies against the glyphs, in EM, y down. (0,0) by default.</summary>
	public static readonly DependencyProperty<TextBlock, Point> ShadowOffsetProperty =
		DependencyProperty.RegisterAttached("ShadowOffset",
			new PropertyMetadata<TextBlock, Point> { Validate = static (_, value) => ValidateOffset(value), PropertyChanged = static (text, _, _) => text.OnTextEffectChanged() });

	/// <summary>The width of the shadow's edge, in EM: it fades from full to none across a band this wide, centred on the
	/// moved glyphs' edge. 0 by default: a hard shadow.</summary>
	public static readonly DependencyProperty<TextBlock, float> ShadowSoftnessProperty =
		DependencyProperty.RegisterAttached("ShadowSoftness",
			new PropertyMetadata<TextBlock, float> { Validate = static (text, value) => ValidateSoftness(text, value, text.AttachedRoot?.Font), PropertyChanged = static (text, _, _) => text.OnTextEffectChanged() });

	/// <summary>The shadow's paint, a colour or a linear gradient over the text's layout box, moved with the shadow. Null
	/// by default: no shadow.</summary>
	public static readonly DependencyProperty<TextBlock, Brush?> ShadowBrushProperty =
		DependencyProperty.RegisterAttached("ShadowBrush",
			new PropertyMetadata<TextBlock, Brush?> { Validate = static (_, brush) => ValidateBrush(brush), PropertyChanged = static (text, old, @new) => text.OnTextEffectBrushChanged(old, @new) });

	public static void SetOutlineThickness(TextBlock element, float value) => element.SetValue(OutlineThicknessProperty, value);
	public static float GetOutlineThickness(TextBlock element) => element.GetValue(OutlineThicknessProperty);

	public static void SetOutlineBrush(TextBlock element, Brush? value) => element.SetValue(OutlineBrushProperty, value);
	public static Brush? GetOutlineBrush(TextBlock element) => element.GetValue(OutlineBrushProperty);

	public static void SetShadowOffset(TextBlock element, Point value) => element.SetValue(ShadowOffsetProperty, value);
	public static Point GetShadowOffset(TextBlock element) => element.GetValue(ShadowOffsetProperty);

	public static void SetShadowSoftness(TextBlock element, float value) => element.SetValue(ShadowSoftnessProperty, value);
	public static float GetShadowSoftness(TextBlock element) => element.GetValue(ShadowSoftnessProperty);

	public static void SetShadowBrush(TextBlock element, Brush? value) => element.SetValue(ShadowBrushProperty, value);
	public static Brush? GetShadowBrush(TextBlock element) => element.GetValue(ShadowBrushProperty);

	/// <summary>
	/// Check the effects of every <see cref="TextBlock"/> under <paramref name="element"/> against <paramref name="font"/>,
	/// the font they are about to draw with, throwing on the first its field cannot hold: called before a tree becomes a
	/// root's content or overlay, and before a root's font changes, so nothing has changed when it throws.
	/// </summary>
	internal static void ValidateTree(UIElement element, FontAtlas font)
	{
		if (element is TextBlock text)
		{
			ValidateOutline(text, GetOutlineThickness(text), font);
			ValidateSoftness(text, GetShadowSoftness(text), font);
		}
		var children = element.VisualChildren;
		for (var i = 0; i < children.Count; i++)
			ValidateTree(children[i], font);
	}

	private static void ValidateOutline(TextBlock text, float thickness, FontAtlas? font)
	{
		if (!(thickness >= 0f) || float.IsPositiveInfinity(thickness))
			throw new ArgumentOutOfRangeException("value", thickness, "An outline's thickness is a distance in EM, 0 or more.");
		if (font is not null && thickness > font.DistanceRangeEm / 2f)
			throw new ArgumentOutOfRangeException("value", thickness, string.Create(CultureInfo.InvariantCulture,
				$"An outline {thickness:0.###} EM thick on {Describe(text)} reaches past the {font.DistanceRangeEm / 2f:0.###} EM its font's distance field holds past each edge. Make it thinner, or bake the font with a wider distance range."));
	}

	private static void ValidateSoftness(TextBlock text, float softness, FontAtlas? font)
	{
		if (!(softness >= 0f) || float.IsPositiveInfinity(softness))
			throw new ArgumentOutOfRangeException("value", softness, "A shadow's softness is a distance in EM, 0 or more.");
		if (font is not null && softness > font.DistanceRangeEm)
			throw new ArgumentOutOfRangeException("value", softness, string.Create(CultureInfo.InvariantCulture,
				$"A shadow {softness:0.###} EM soft on {Describe(text)} fades {softness / 2f:0.###} EM past its edge, past the {font.DistanceRangeEm / 2f:0.###} EM its font's distance field holds. Make it sharper, or bake the font with a wider distance range."));
	}

	private static void ValidateOffset(Point offset)
	{
		if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
			throw new ArgumentOutOfRangeException("value", offset, "A shadow's offset is a distance in EM, and finite.");
	}

	private static void ValidateBrush(Brush? brush)
	{
		if (brush is TileBrush)
			throw new NotSupportedException(
				"A text effect is painted by a colour or a linear gradient: glyphs are drawn from the font atlas, and a glyph prim samples one texture.");
	}

	private static string Describe(TextBlock text) => $"the TextBlock \"{text.Text}\"";
}
