using System.Numerics;
using SUIsei.Composition;

namespace SUIsei.Drawing;

/// <summary>
/// Builds a <see cref="DrawListData"/> by hand, with the draw list the compositor uses, for a host's own overlays, demos
/// and tests. It covers vector geometry from the <see cref="Tessellator"/>, in a solid colour or a texture, and group
/// layers, and nothing else: no gradients, rects or glyphs, which only <see cref="DrawListCompositor"/>'s walk emits.
/// The full vector seam is the tessellator's own: <see cref="Tessellator"/>, <see cref="TessellatedPath"/>,
/// <see cref="IVectorSink"/> and <see cref="VectorVertex"/>, from which a foreign <see cref="IUICompositor"/> draws
/// paths its own way.
/// <para>
/// Like a compositor's surface, a builder keeps two sets of buffers and fills them in turn: the list one
/// <see cref="End"/> returns stays readable through the next build, its streams throw once the build after that reuses
/// them, and building lists of a steady size allocates nothing.
/// </para>
/// <para>
/// The lists it builds sit outside texture residency: unless a compositor hands them to a <see cref="UIHost"/> as an
/// <see cref="IDrawListSource"/>, nothing reports the textures they sample in <see cref="UIHost.ReleasedTextures"/>,
/// and whoever draws them keeps and releases those textures itself.
/// </para>
/// </summary>
public sealed class DrawListBuilder
{
	private readonly UIDrawList _list = new();

	/// <summary>Start a list for a surface of <paramref name="surfaceSize"/> pixels.</summary>
	public void Begin(Vector2 surfaceSize) => _list.Begin(surfaceSize);

	/// <summary>Append a fill or a whole stroke, transformed into surface pixels, in a solid colour.</summary>
	public void AddVector(TessellatedPath path, in Matrix3x2 transform, PremultipliedHdrColor color, in Rect clip) =>
		_list.AddVector(path, PathTrim.None, transform, color, clip);

	/// <summary>
	/// Append a fill or a stroke trimmed by <paramref name="trim"/>, transformed into surface pixels and drawn in
	/// <paramref name="color"/> times <paramref name="texture"/> (the white texel when null), sampled at
	/// <paramref name="localToUv"/> of each local position. Without <paramref name="localToUv"/> the texture is stretched
	/// over the path's <see cref="TessellatedPath.Bounds"/>, 0 to 1 across them. With <paramref name="groupOpacity"/>
	/// below 1 it is drawn in a group layer of its own whose tint carries that opacity, so its overlaps blend once: pass
	/// an opaque colour and the alpha as the group opacity to draw a translucent stroke whose joins and crossings match
	/// the rest of it.
	/// </summary>
	public void AddVector(TessellatedPath path, PathTrim trim, in Matrix3x2 transform, PremultipliedHdrColor color, in Rect clip,
		float groupOpacity = 1f, TextureSource? texture = null, Matrix3x2? localToUv = null) =>
		_list.AddVector(path, trim, transform, color, clip, groupOpacity, texture, localToUv ?? Stretched(path.Bounds));

	// The map from local positions to uv that puts 0 to 1 across `bounds`, or identity for bounds with no area.
	private static Matrix3x2 Stretched(Rect bounds) => bounds.Width > 0f && bounds.Height > 0f
		? Matrix3x2.CreateTranslation(-bounds.X, -bounds.Y) * Matrix3x2.CreateScale(1f / bounds.Width, 1f / bounds.Height)
		: Matrix3x2.Identity;

	/// <summary>Open a group layer clipped to <paramref name="clip"/>, composited back at <paramref name="opacity"/>; its
	/// rect becomes the bounds of what is drawn in it.</summary>
	public void BeginLayer(in Rect clip, float opacity) => _list.BeginLayer(clip, opacity, Vector4.Zero);

	/// <summary>Close the innermost open layer; <paramref name="clip"/> scissors its composite.</summary>
	public void EndLayer(in Rect clip) => _list.EndLayer(clip);

	/// <summary>
	/// Finish the list. Its <see cref="DrawListData.Damage"/> is <paramref name="damage"/>, the region that changed since the
	/// list built before it, or the whole surface without it: a builder does not compare one list with the last, and a list
	/// whose damage said nothing changed would never be drawn by a host that repaints only damage.
	/// </summary>
	public DrawListData End(bool hdr = false, Rect? damage = null)
	{
		var list = _list.End(hdr);
		list.StampDamage(damage ?? new Rect(0f, 0f, list.CoordinateSize.X, list.CoordinateSize.Y));
		return list;
	}
}
