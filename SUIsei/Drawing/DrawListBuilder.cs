using System.Numerics;
using SUIsei.Composition;

namespace SUIsei.Drawing;

/// <summary>
/// Builds a <see cref="DrawListData"/> by hand, with the draw list the compositor uses: vector geometry from the
/// <see cref="Tessellator"/>, and group layers. It is the public way from tessellated paths to vertices and commands,
/// for a foreign <see cref="IUICompositor"/> that wants SUIsei's draw contract, a host's own overlays, and tests.
/// <para>
/// Like a compositor's surface, a builder keeps two sets of buffers and fills them in turn: the list one
/// <see cref="End"/> returns stays readable through the next build, its streams throw once the build after that reuses
/// them, and building lists of a steady size allocates nothing.
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
	/// <paramref name="localToUv"/> of each local position. With <paramref name="groupOpacity"/> below 1 it is drawn in a
	/// group layer of its own whose tint carries that opacity, so its overlaps blend once: pass an opaque colour and the
	/// alpha as the group opacity to draw a translucent stroke whose joins and crossings match the rest of it.
	/// </summary>
	public void AddVector(TessellatedPath path, PathTrim trim, in Matrix3x2 transform, PremultipliedHdrColor color, in Rect clip,
		float groupOpacity = 1f, TextureSource? texture = null, Matrix3x2 localToUv = default) =>
		_list.AddVector(path, trim, transform, color, clip, groupOpacity, texture, localToUv);

	/// <summary>Open a group layer clipped to <paramref name="clip"/>, composited back at <paramref name="opacity"/>; its
	/// rect becomes the bounds of what is drawn in it.</summary>
	public void BeginLayer(in Rect clip, float opacity) => _list.BeginLayer(clip, opacity, Vector4.Zero);

	/// <summary>Close the innermost open layer; <paramref name="clip"/> scissors its composite.</summary>
	public void EndLayer(in Rect clip) => _list.EndLayer(clip);

	/// <summary>Finish the list.</summary>
	public DrawListData End(bool hdr = false) => _list.End(hdr);
}
