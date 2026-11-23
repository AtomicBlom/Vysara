namespace BinaryVibrance.Vysara;

/// <summary>What a glyph run's quads draw (<see cref="UIDrawList.AddGlyphRun"/>): the glyphs, or one of the effects
/// drawn from the atlas's true-distance channel under them.</summary>
internal enum GlyphLayer
{
	/// <summary>The glyphs themselves, from the median of the atlas's RGB distances.</summary>
	Fill,

	/// <summary>The glyphs grown by the run's outline thickness.</summary>
	Outline,

	/// <summary>The glyphs moved by the run's shadow offset, their edge as soft as its softness.</summary>
	Shadow,
}
