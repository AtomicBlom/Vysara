using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// Loom's default <see cref="IUICompositor"/>: walks a surface's composition tree, folds transform/clip/opacity
/// into a pre-transformed vertex/command stream, and exposes it as a neutral <see cref="DrawListData"/>. Emits the
/// rectangle family (fills + borders) and MSDF glyph runs. Output is asserted on CPU via
/// <see cref="DrawListData.Dump"/> — the primary test seam, no backend needed.
/// </summary>
public sealed class DrawListCompositor : IUICompositor
{
	// Each surface's builder, kept across composites: it owns the surface's retained buffers and its revision.
	private readonly Dictionary<int, UIDrawList> _surfaces = [];
	private readonly FontAtlas _font;

	/// <param name="font">
	/// The atlas glyph runs lay out against; defaults to <see cref="FontAtlas.Default"/> (the embedded default font).
	/// Must be the same atlas the renderer uploaded via <c>EnableUI(font.Texture)</c> so UVs line up.
	/// </param>
	public DrawListCompositor(FontAtlas? font = null) => _font = font ?? FontAtlas.Default;

	/// <summary>How many full recomposites have run (a clean frame's no-op does not increment it) — instrumentation
	/// for the incremental-recomposition tests.</summary>
	public int CompositeCount { get; private set; }

	public void Composite(RootVisual root, DrawListId surface)
	{
		// Frame-level no-op: once a surface has been composited, a fully-clean visual
		// tree re-submits the cached draw data with the same Revision — a static frame does zero compositing work
		// (and the backend can skip re-upload). A visual-only change marks its subtree dirty and forces a rebuild.
		if (!_surfaces.TryGetValue(surface.Value, out var list))
			_surfaces[surface.Value] = list = new UIDrawList();
		else if (!AnyDirty(root))
			return;

		var surfaceRect = new Rect(0, 0, root.SurfaceSize.X, root.SurfaceSize.Y);
		list.Begin(root.SurfaceSize);
		Walk(list, root, Matrix3x2.Identity, 1f, surfaceRect);
		var built = list.End(root.IsHdrSurface);
		built.Damage = list.Previous.Revision == 0
			? surfaceRect // first build for this surface → repaint all
			: ComputeDamage(list.Previous, list.Current, root.SurfaceSize);
		CompositeCount++;
	}

	// The pixel region that differs between two consecutive draw lists of the same surface — the union of the old and
	// new positions of every vertex whose contents changed (so colour fades, moves, and resizes are all covered). A
	// structural change (commands added/removed/reordered, or a different vertex count) or any group-opacity layer
	// expands to the whole surface, since those can affect pixels beyond the literal vertex deltas. The previous list
	// is still intact in the other buffer set, so this compares the two in place.
	private static Rect ComputeDamage(DrawListBuffers previous, DrawListBuffers current, Vector2 surfaceSize)
	{
		var pc = previous.Commands.Span;
		var cc = current.Commands.Span;
		var pv = previous.Vertices.Span;
		var cv = current.Vertices.Span;

		if (pv.Length != cv.Length || pc.Length != cc.Length || !pc.SequenceEqual(cc) || HasLayers(cc))
			return new Rect(0, 0, surfaceSize.X, surfaceSize.Y);

		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		var any = false;
		for (var i = 0; i < cv.Length; i++)
		{
			if (cv[i].Equals(pv[i]))
				continue;
			Grow(ref minX, ref minY, ref maxX, ref maxY, pv[i].Position); // old position — erases where it was
			Grow(ref minX, ref minY, ref maxX, ref maxY, cv[i].Position); // new position — paints where it is
			any = true;
		}
		return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.Zero;
	}

	private static void Grow(ref float minX, ref float minY, ref float maxX, ref float maxY, Vector2 p)
	{
		minX = MathF.Min(minX, p.X); minY = MathF.Min(minY, p.Y);
		maxX = MathF.Max(maxX, p.X); maxY = MathF.Max(maxY, p.Y);
	}

	private static bool HasLayers(ReadOnlySpan<DrawCommand> commands)
	{
		foreach (ref readonly var cmd in commands)
			if ((UIPrimKind)cmd.Tag is UIPrimKind.BeginLayer or UIPrimKind.EndLayer)
				return true;
		return false;
	}

	private static bool AnyDirty(Visual v)
	{
		if (v.IsDirty)
			return true;
		// avoid the enumerator as this is often called on the hot path of an engine.
		for (var index = 0; index < v.Children.Count; index++)
		{
			var c = v.Children[index];
			if (AnyDirty(c))
				return true;
		}

		return false;
	}

	/// <summary>
	/// The last <see cref="DrawListData"/> produced for <paramref name="surface"/> (handed to <see cref="UIHost.DrawLists"/>).
	/// It is a view over buffers this compositor reuses: read it this frame, and ask again next frame rather than keeping it.
	/// </summary>
	public DrawListData LastDraw(DrawListId surface) => _surfaces.TryGetValue(surface.Value, out var list) ? list.Last : DrawListData.Empty;

	private void Walk(UIDrawList list, Visual v, Matrix3x2 parent, float opacity, Rect clip)
	{
		v.IsDirty = false; // composited this frame — clean until the next visual-only change marks it again
		var matrix = v.Transform * parent;
		var localClip = clip;
		if (v.Clip is { } c)
			localClip = clip.Intersect(c.TransformBounds(matrix));

		// Children are walked by index: a foreach over IReadOnlyList<Visual> would box an enumerator per visual per frame.
		var children = v.Children;

		// A container that fades a subtree which can overlap (sub-1 opacity with children), or carries a group effect,
		// must composite as one render-to-texture layer — otherwise overlapping children double-blend against each
		// other through the folded per-vertex alpha. Wrap the subtree in a BeginLayer/EndLayer pair and emit it at
		// full alpha; the backend renders it to a transient target and fades it once on composite-back. A childless
		// faded sprite has no overlap, so it stays on the cheap per-vertex fold.
		if (NeedsLayer(v) && !localClip.IsEmpty)
		{
			list.BeginLayer(localClip, v.Opacity, v.Effect?.ToParams() ?? Vector4.Zero);
			EmitContent(list, v, matrix, 1f, localClip);
			for (var i = 0; i < children.Count; i++)
				Walk(list, children[i], matrix, 1f, localClip);
			list.EndLayer(clip);
			return;
		}

		var alpha = opacity * v.Opacity;
		EmitContent(list, v, matrix, alpha, localClip);
		for (var i = 0; i < children.Count; i++)
			Walk(list, children[i], matrix, alpha, localClip);
	}

	// A subtree needs its own composite layer when it fades a group of children (overlap would double-blend) or
	// applies a group effect. A leaf sprite folds its opacity into vertex alpha with no double-blend.
	private static bool NeedsLayer(Visual v) => v.Effect is not null || (v.Opacity < 1f && v.Children.Count > 0);

	private void EmitContent(UIDrawList list, Visual v, in Matrix3x2 matrix, float alpha, in Rect localClip)
	{
		if (v is not SpriteVisual sprite)
			return;

		switch (sprite.Content)
		{
			case RectContent rc:
				{
					var rect = new Rect(0, 0, rc.Size.X, rc.Size.Y);
					if (rc.Fill is { } fill)
					{
						var kind = rc.CornerRadius > 0 ? UIPrimKind.RoundedRect : UIPrimKind.Fill;
						// params = (cornerRadius, borderThickness, sizeX, sizeY) — the layout ui.slang's analytic rounded-box
						// SDF reads (uv is 0..1 across the rect; the shader scales by params.zw to local pixels). Fill ignores it.
						list.AddRect(rect, matrix, fill.ToPremultiplied(alpha), kind, new Vector4(rc.CornerRadius, 0f, rc.Size.X, rc.Size.Y), localClip);
					}
					if (rc.BorderBrush is { } bb && rc.BorderThickness > 0)
						list.AddRect(rect, matrix, bb.ToPremultiplied(alpha), UIPrimKind.Border, new Vector4(rc.CornerRadius, rc.BorderThickness, rc.Size.X, rc.Size.Y), localClip);
					break;
				}
			case GlyphRunContent gr:
				list.AddGlyphRun(gr.Text, gr.FontSizePx, gr.Color.ToPremultiplied(alpha), _font, matrix, localClip);
				break;
			case ImageContent ic:
				// White tint (image unchanged) folded by the subtree opacity — the shader does colour *= texSample.
				list.AddImage(ic.Dest, matrix, PremultipliedHdrColor.FromStraight(1f, 1f, 1f, alpha), ic.Source, localClip);
				break;
		}
	}
}
