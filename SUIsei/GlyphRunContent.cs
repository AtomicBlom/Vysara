namespace SUIsei;

/// <summary>
/// A run of laid-out text drawn from a <see cref="FontAtlas"/>. The compositor lays the glyphs
/// out against its font (advance-based LTR, no shaping), emitting <see cref="Glyph"/>
/// commands. <paramref name="FontSizePx"/> is the on-screen size in surface pixels (DIPs × scale); the colour is
/// the authored <see cref="HdrColor"/> (linearized + premultiplied by the compositor like every other brush).
/// </summary>
public sealed record GlyphRunContent(string Text, float FontSizePx, HdrColor Color) : VisualContent;
