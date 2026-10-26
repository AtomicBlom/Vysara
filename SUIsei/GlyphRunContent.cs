using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei;

/// <summary>
/// A run of laid-out text drawn from a <see cref="FontAtlas"/>. The compositor lays the glyphs out against its font
/// through the atlas's shaper, emitting <see cref="UIPrimKind.Glyph"/> commands. <see cref="FontSizePx"/> is the
/// on-screen size in surface pixels (DIPs × scale); <see cref="Brush"/> colours the glyphs, a gradient measured over
/// <see cref="Size"/>.
/// </summary>
public sealed class GlyphRunContent(string text, float fontSizePx, CompositionBrush? brush, Vector2 size = default) : VisualContent
{
	/// <summary>An empty run, for an element to fill in with <see cref="Update"/>.</summary>
	public GlyphRunContent() : this("", 0f, null)
	{
	}

	/// <summary>The text to lay out.</summary>
	public string Text { get; set => Change(ref field, value); } = text;

	/// <summary>The font size in surface pixels.</summary>
	public float FontSizePx { get; set => Change(ref field, value); } = fontSizePx;

	/// <summary>The glyphs' paint, or null to draw nothing. Changes inside the brush are versioned by the brush, not by
	/// this content.</summary>
	public CompositionBrush? Brush { get; set => Change(ref field, value); } = brush;

	/// <summary>The box a gradient brush is measured over, in the visual's local space from (0,0): the text element's
	/// layout size.</summary>
	public Vector2 Size { get; set => Change(ref field, value); } = size;

	/// <summary>Set everything this content draws at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void Update(string text, float fontSizePx, CompositionBrush? brush, Vector2 size)
	{
		Text = text;
		FontSizePx = fontSizePx;
		Brush = brush;
		Size = size;
	}

	internal override void Describe(StringBuilder sb)
	{
		sb.Append(CultureInfo.InvariantCulture, $" glyphs=\"{Text}\" size={FontSizePx:0.###}");
		if (Brush is { } b)
		{
			sb.Append(" brush=");
			b.Describe(sb);
		}
	}
}
