namespace Vysara;

/// <summary>
/// One positioned glyph produced by an <see cref="ITextShaper"/>: the atlas <see cref="Glyph"/> to draw, the pen
/// position and per-glyph offsets it sits at (all in EM units, baseline at the origin — the layout scales them by
/// the on-screen font px size), and the source-text <see cref="Cluster"/> (the index of the character it derives
/// from, for caret / hit-test mapping). Whitespace and other zero-geometry glyphs are omitted from a shaped run.
/// </summary>
public readonly record struct ShapedGlyph(Glyph Glyph, float PenXEm, float XOffsetEm, float YOffsetEm, int Cluster);
