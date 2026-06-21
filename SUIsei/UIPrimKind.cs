namespace SUIsei;

/// <summary>
/// The primitive family the UI compositor encodes into a draw command's <see cref="Drawing.DrawCommand.Tag"/>
/// — it selects the branch the single <c>ui.slang</c> fragment switches on. A flat <see cref="byte"/> so it casts to
/// the tag cheaply.
/// </summary>
public enum UIPrimKind : byte
{
	/// <summary>A solid axis-aligned fill (white-texel sampled).</summary>
	Fill,

	/// <summary>An analytic rounded rectangle (radii in <see cref="Drawing.OverlayVertex.Custom"/>).</summary>
	RoundedRect,

	/// <summary>An analytic (optionally rounded) border stroke (half-width in <see cref="Drawing.OverlayVertex.Custom"/>).</summary>
	Border,

	/// <summary>An MSDF glyph sampled from a font atlas page.</summary>
	Glyph,

	/// <summary>A tinted image sampled from a UI image atlas or an in-world source target.</summary>
	Image,

	/// <summary>
	/// Opens a render-to-texture group layer: the backend redirects subsequent draws into a transient offscreen
	/// target until the matching <see cref="EndLayer"/>. A zero-geometry marker (no indices) — its
	/// <see cref="Drawing.DrawCommand.ClipRect"/> is the layer's surface-pixel bounds, and its single degenerate quad
	/// at <see cref="Drawing.DrawCommand.VertexOffset"/> carries the premultiplied fade tint in
	/// <see cref="Drawing.OverlayVertex.Color"/> and the effect id+params in <see cref="Drawing.OverlayVertex.Custom"/>.
	/// Never reaches the fragment shader.
	/// </summary>
	BeginLayer,

	/// <summary>
	/// Closes the most-recently-opened layer: the backend resolves the offscreen target and composites it back into
	/// the parent at the group's fade alpha (a tinted blit). A zero-geometry marker; never reaches the fragment shader.
	/// </summary>
	EndLayer,
}
