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
/// glyphs, a gradient measured over <see cref="Size"/>.
/// </summary>
public sealed class GlyphRunContent(string text, float fontSize, CompositionBrush? brush, Vector2 size = default,
	FontAtlas? font = null, FontFeatureSet? features = null) : VisualContent
{
	/// <summary>An empty run, for an element to fill in with <see cref="Update"/>.</summary>
	public GlyphRunContent() : this("", 0f, null)
	{
	}

	/// <summary>The text to lay out.</summary>
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

	internal override void Describe(StringBuilder sb)
	{
		sb.Append(CultureInfo.InvariantCulture, $" glyphs=\"{Text}\" size={FontSize:0.###}");
		if (!Features.IsEmpty)
			sb.Append(CultureInfo.InvariantCulture, $" features=\"{Features}\"");
		if (Brush is { } b)
		{
			sb.Append(" brush=");
			b.Describe(sb);
		}
	}
}
