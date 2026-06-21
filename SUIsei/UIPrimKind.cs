namespace SUIsei;

/// <summary>
/// The primitive family the UI compositor encodes into a draw command's <see cref="Loom.Core.Hosting.DrawCommand.Tag"/>
/// — it selects the branch the single <c>ui.slang</c> fragment switches on. A flat <see cref="byte"/> so it casts to
/// the tag cheaply.
/// </summary>
public enum UIPrimKind : byte
{
	/// <summary>A solid axis-aligned fill (white-texel sampled).</summary>
	Fill,

	/// <summary>An analytic rounded rectangle (radii in <see cref="Loom.Core.Geometry.OverlayVertex.Custom"/>).</summary>
	RoundedRect,

	/// <summary>An analytic (optionally rounded) border stroke (half-width in <see cref="Loom.Core.Geometry.OverlayVertex.Custom"/>).</summary>
	Border,

	/// <summary>An MSDF glyph sampled from a font atlas page.</summary>
	Glyph,

	/// <summary>A tinted image sampled from a UI image atlas or an in-world source target.</summary>
	Image,
}
