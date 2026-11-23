namespace BinaryVibrance.Vysara;

/// <summary>
/// The primitive family the UI compositor encodes into a draw command's <see cref="Drawing.DrawCommand.Tag"/>
/// — it selects the branch the single <c>ui.slang</c> fragment switches on. A flat <see cref="byte"/> so it casts to
/// the tag cheaply.
/// <para>
/// Vysara sets only a tag's low byte: a tag it emits is exactly one of these kinds, and the bits from 0x100 up are
/// always clear. They are reserved for a host's own per-command state, which it derives into its own copy of the
/// commands (Loom's painter marks a command whose texture slot holds straight-alpha texels with 0x100). A host that does
/// so reads the kind from the low byte; on a list as Vysara hands it out, any other tag is still a broken list.
/// </para>
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

	/// <summary>
	/// Tessellated vector geometry (fills and strokes), antialiased by signed distances.
	/// <see cref="Drawing.OverlayVertex.Custom"/> holds four distances <c>d0..d3</c> to the edges the triangle antialiases,
	/// positive inside, each linear across the triangle and in any consistent unit; an unused one holds
	/// <see cref="Drawing.VectorVertex.Unused"/>, constant across the triangle. The fragment's coverage is
	/// <c>Π saturate(0.5 + dᵢ / max(length(float2(ddx dᵢ, ddy dᵢ)), 1e-6))</c>: each distance over its screen-space
	/// gradient's length is the pixel distance to its edge, and an unused slot's zero gradient makes its factor 1. The
	/// colour is the vertex colour times that coverage times the command's texture sampled at
	/// <see cref="Drawing.OverlayVertex.Uv"/> (slot 0, the white texel, for a solid colour).
	/// </summary>
	Vector,
}
