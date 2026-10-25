using System.Globalization;
using System.Text;

namespace SUIsei;

/// <summary>
/// A run of laid-out text drawn from a <see cref="FontAtlas"/>. The compositor lays the glyphs
/// out against its font (advance-based LTR, no shaping), emitting <see cref="Glyph"/>
/// commands. <see cref="FontSizePx"/> is the on-screen size in surface pixels (DIPs × scale); the colour is
/// the authored <see cref="HdrColor"/> (linearized + premultiplied by the compositor like every other brush).
/// </summary>
public sealed class GlyphRunContent(string text, float fontSizePx, HdrColor color) : VisualContent
{
	/// <summary>An empty run, for an element to fill in with <see cref="Update"/>.</summary>
	public GlyphRunContent() : this("", 0f, default)
	{
	}

	/// <summary>The text to lay out.</summary>
	public string Text { get; set => Change(ref field, value); } = text;

	/// <summary>The font size in surface pixels.</summary>
	public float FontSizePx { get; set => Change(ref field, value); } = fontSizePx;

	/// <summary>The glyph colour.</summary>
	public HdrColor Color { get; set => Change(ref field, value); } = color;

	/// <summary>Set everything this content draws at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void Update(string text, float fontSizePx, HdrColor color)
	{
		Text = text;
		FontSizePx = fontSizePx;
		Color = color;
	}

	internal override void Describe(StringBuilder sb) =>
		sb.Append(CultureInfo.InvariantCulture, $" glyphs=\"{Text}\" size={FontSizePx:0.###}");
}
