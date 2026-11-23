using BinaryVibrance.Vysara.Text.Geometry;

namespace BinaryVibrance.Vysara.Text.OpenType;

/// <summary>One outline point in font units with its on/off-curve flag (the TrueType <c>glyf</c> point representation).</summary>
internal readonly record struct GlyphContourPoint(Vector2D Position, bool OnCurve);
