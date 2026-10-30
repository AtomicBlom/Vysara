using System.Globalization;
using SUIsei.Composition;
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
/// edge; at a small size, less, since the edge's own ramp takes some of the field. An effect its font's field cannot
/// hold at the size it is drawn is drawn clamped to what it can, whatever the order the text, its font and its root
/// were put together in, and the text's visual says so in <see cref="Visual.Dump()"/>. A negative or non-finite size
/// throws whenever it is set, as does an animation that names one (its start and end, its keyframes) as it begins.
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
			new PropertyMetadata<TextBlock, float> { Validate = static (_, value) => ValidateOutline(value), PropertyChanged = static (text, _, _) => text.OnTextEffectChanged() });

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
			new PropertyMetadata<TextBlock, float> { Validate = static (_, value) => ValidateSoftness(value), PropertyChanged = static (text, _, _) => text.OnTextEffectChanged() });

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

	// Only the value itself is checked where it is set: what the font's field can hold depends on the font and the size
	// the text is drawn at, which the emitter clamps to.
	private static void ValidateOutline(float thickness)
	{
		if (!(thickness >= 0f) || float.IsPositiveInfinity(thickness))
			throw new ArgumentOutOfRangeException("value", thickness, "An outline's thickness is a distance in EM, 0 or more.");
	}

	private static void ValidateSoftness(float softness)
	{
		if (!(softness >= 0f) || float.IsPositiveInfinity(softness))
			throw new ArgumentOutOfRangeException("value", softness, "A shadow's softness is a distance in EM, 0 or more.");
	}

	private static void ValidateOffset(Point offset)
	{
		if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
			throw new ArgumentOutOfRangeException("value", offset, "A shadow's offset is a distance in EM, and finite.");
	}

	private static void ValidateBrush(Brush? brush)
	{
		if (brush is TileBrush)
			throw new ArgumentException(
				"A text effect is painted by a colour or a linear gradient: glyphs are drawn from the font atlas, and a glyph prim samples one texture.", "value");
	}
}
