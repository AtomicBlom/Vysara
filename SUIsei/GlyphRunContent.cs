using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei;

/// <summary>
/// A run of laid-out text drawn from a <see cref="FontAtlas"/>. The compositor lays the glyphs out against
/// <see cref="Font"/> through the atlas's shaper, with <see cref="Features"/> applied, emitting
/// <see cref="UIPrimKind.Glyph"/> commands: the font and features the element measured the run with, so it draws in the
/// space layout gave it. <see cref="FontSize"/> is in the visual's own units, as an element's layout is (DIPs); the
/// transforms above it, the root's DIP→pixel scale included, take it to the surface. <see cref="Brush"/> colours the
/// glyphs, a gradient measured over <see cref="Size"/>. The text breaks into lines at its line breaks, each placed across
/// <see cref="Size"/>'s width by <see cref="TextAlignment"/>.
/// <para>
/// A run can carry two effects drawn from the atlas's true-distance channel, both sized in EM so they scale with the
/// text: an outline (<see cref="OutlineThickness"/>, <see cref="OutlineBrush"/>), the glyphs grown by the thickness, and a
/// shadow (<see cref="ShadowOffset"/>, <see cref="ShadowSoftness"/>, <see cref="ShadowBrush"/>), the glyphs moved and
/// blurred. The compositor draws every glyph's shadow, then every glyph's outline, then the glyphs, so no effect lands on
/// a neighbouring glyph. The field reaches <see cref="FontAtlas.DistanceRangeEm"/> / 2 past each edge, and an effect
/// reaching further is drawn clamped to it; <c>suisei:TextEffects</c> validates the sizes it hands a run against the font.
/// </para>
/// </summary>
public sealed class GlyphRunContent(string text, float fontSize, CompositionBrush? brush, Vector2 size = default,
	FontAtlas? font = null, FontFeatureSet? features = null) : VisualContent
{
	/// <summary>An empty run, for an element to fill in with <see cref="Update"/>.</summary>
	public GlyphRunContent() : this("", 0f, null)
	{
	}

	/// <summary>The text to lay out. Each line break (a line feed, a carriage return, or another of Unicode's mandatory
	/// breaks) starts a new line, the font's line height below the last, placed across <see cref="Size"/> by
	/// <see cref="TextAlignment"/>.</summary>
	public string Text { get; set => Change(ref field, value); } = text;

	/// <summary>The font size, the EM, in the visual's own units (DIPs for an element's text).</summary>
	public float FontSize { get; set => Change(ref field, value); } = fontSize;

	/// <summary>The glyphs' paint, or null to draw nothing. Changes inside the brush are versioned by the brush, not by
	/// this content.</summary>
	public CompositionBrush? Brush { get; set => Change(ref field, value); } = brush;

	/// <summary>The box a gradient brush is measured over, in the visual's local space from (0,0): the text element's
	/// layout size.</summary>
	public Vector2 Size { get; set => Change(ref field, value); } = size;

	/// <summary>The atlas the run is shaped against and drawn from; <see cref="FontAtlas.Default"/> unless set.</summary>
	public FontAtlas Font { get; set => Change(ref field, value ?? throw new ArgumentNullException(nameof(value))); } = font ?? FontAtlas.Default;

	/// <summary>The OpenType features the run is shaped with; <see cref="FontFeatureSet.Empty"/> unless set. Compared by
	/// value, so an equal set does not advance <see cref="VisualContent.Version"/>.</summary>
	public FontFeatureSet Features { get; set => Change(ref field, value ?? throw new ArgumentNullException(nameof(value))); } = features ?? FontFeatureSet.Empty;

	/// <summary>How each line sits across <see cref="Size"/>'s width; <see cref="SUIsei.TextAlignment.Left"/> unless set.
	/// Whitespace at a line's end hangs past where the line is placed.</summary>
	public TextAlignment TextAlignment { get; set => Change(ref field, value); } = TextAlignment.Left;

	/// <summary>Where <paramref name="line"/>'s pen starts across <see cref="Size"/>'s width, in the visual's units.</summary>
	internal float LineStart(in ShapedLine line) => TextAlignment switch
	{
		TextAlignment.Center => (Size.X - line.PlacedWidthEm * FontSize) / 2f,
		TextAlignment.Right => Size.X - line.PlacedWidthEm * FontSize,
		_ => 0f, // Left (Start), and Justify: no line wraps, so none is stretched
	};

	/// <summary>How far the outline reaches past the glyphs' edges, in EM. 0, the default, draws no outline.</summary>
	public float OutlineThickness { get; set => Change(ref field, value); }

	/// <summary>The outline's paint, a colour or a linear gradient measured over <see cref="Size"/>; null draws no
	/// outline.</summary>
	public CompositionBrush? OutlineBrush { get; set => Change(ref field, value); }

	/// <summary>Where the shadow lies against the glyphs, in EM, y down.</summary>
	public Vector2 ShadowOffset { get; set => Change(ref field, value); }

	/// <summary>The width of the shadow's edge, in EM: it fades from full to none across a band this wide, centred on
	/// the glyphs' outline. 0, the default, gives a hard shadow.</summary>
	public float ShadowSoftness { get; set => Change(ref field, value); }

	/// <summary>The shadow's paint, a colour or a linear gradient measured over <see cref="Size"/> and moved with the
	/// shadow; null draws no shadow.</summary>
	public CompositionBrush? ShadowBrush { get; set => Change(ref field, value); }

	/// <summary>Whether the run draws an outline or a shadow.</summary>
	internal bool HasEffects => (OutlineBrush is not null && OutlineThickness > 0f) || ShadowBrush is not null;

	/// <summary>Set the run's effects at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void UpdateEffects(float outlineThickness, CompositionBrush? outlineBrush, Vector2 shadowOffset, float shadowSoftness,
		CompositionBrush? shadowBrush)
	{
		OutlineThickness = outlineThickness;
		OutlineBrush = outlineBrush;
		ShadowOffset = shadowOffset;
		ShadowSoftness = shadowSoftness;
		ShadowBrush = shadowBrush;
	}

	/// <summary>Set everything this content draws at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void Update(string text, float fontSize, CompositionBrush? brush, Vector2 size, FontAtlas font, FontFeatureSet features)
	{
		Text = text;
		FontSize = fontSize;
		Brush = brush;
		Size = size;
		Font = font;
		Features = features;
	}

	/// <summary>
	/// This run shaped, cached on the content and keyed by its <see cref="VisualContent.Version"/>: a recomposite of an
	/// unchanged run reads the glyphs it shaped before, so a surface recompositing every frame (an animation beside the text,
	/// a colour animation on it) never re-shapes. When the version has moved the cache checks what shaping reads, the text,
	/// font, features and the font's shaper, and re-shapes only if one of them changed, into the same retained list.
	/// </summary>
	internal ShapedGlyphRun Shape()
	{
		// The shaper can be swapped on the font without the content changing, so even a current run checks it.
		var run = Derived as ShapedGlyphRun;
		if (run is not null && HasCurrentDerived && ReferenceEquals(run.Shaper, Font.Shaper))
			return run;
		run ??= new ShapedGlyphRun();
		if (!run.IsShapingOf(Text, Font, Features))
			run.Reshape(Text, Font, Features);
		SetDerived(run);
		return run;
	}

	internal override void Describe(StringBuilder sb)
	{
		sb.Append(CultureInfo.InvariantCulture, $" glyphs=\"{Text}\" size={FontSize:0.###}");
		if (!Features.IsEmpty)
			sb.Append(CultureInfo.InvariantCulture, $" features=\"{Features}\"");
		if (TextAlignment != TextAlignment.Left)
			sb.Append(TextAlignment switch { TextAlignment.Center => " align=center", TextAlignment.Right => " align=right", _ => " align=justify" });
		if (Brush is { } b)
		{
			sb.Append(" brush=");
			b.Describe(sb);
		}
		if (OutlineBrush is { } outline && OutlineThickness > 0f)
		{
			sb.Append(CultureInfo.InvariantCulture, $" outline={OutlineThickness:0.###}");
			outline.Describe(sb);
		}
		if (ShadowBrush is { } shadow)
		{
			sb.Append(CultureInfo.InvariantCulture, $" shadow={ShadowOffset.X:0.###},{ShadowOffset.Y:0.###}");
			shadow.Describe(sb);
			if (ShadowSoftness > 0f)
				sb.Append(CultureInfo.InvariantCulture, $" softness={ShadowSoftness:0.###}");
		}
	}
}
