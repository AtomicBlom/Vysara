using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei.Composition;

/// <summary>
/// Loom's default <see cref="IUICompositor"/>: walks a surface's composition tree, folds transform/clip/opacity
/// into a pre-transformed vertex/command stream, and exposes it as a neutral <see cref="DrawListData"/>. Emits the
/// rectangle family (fills + borders), MSDF glyph runs and vector shapes (<see cref="ShapeContent"/>, through the
/// <see cref="UIPrimKind.Vector"/> prim), painted by colour, linear-gradient or image brushes; a gradient is drawn exactly
/// by cutting the geometry where its colour stops being linear (<see cref="GradientSplitter"/>). Output is asserted on
/// CPU via <see cref="DrawListData.Dump"/> — the primary test seam, no backend needed.
/// </summary>
public sealed class DrawListCompositor : IUICompositor, IDrawListSource
{
	// Each surface's builder, kept across composites: it owns the surface's retained buffers and its revision.
	private readonly Dictionary<int, UIDrawList> _surfaces = [];

	/// <summary>How many full recomposites have run (a clean frame's no-op does not increment it) — instrumentation
	/// for the incremental-recomposition tests.</summary>
	public int CompositeCount { get; private set; }

	public void Composite(RootVisual root, DrawListId surface)
	{
		// Frame-level no-op: once a surface has been composited, a fully-clean visual
		// tree re-submits the cached draw data with the same Revision and no damage — a static frame does zero compositing
		// work (and the backend can skip re-upload and repaint). A visual-only change marks its subtree dirty and forces a
		// rebuild. A texture source whose texels changed still moves the cached list's TextureRevision and damages what it
		// drew, so the host re-uploads and repaints it.
		if (!_surfaces.TryGetValue(surface.Value, out var list))
			_surfaces[surface.Value] = list = new UIDrawList();
		else if (!AnyDirty(root))
		{
			list.Resubmit();
			return;
		}

		var surfaceRect = new Rect(0, 0, root.SurfaceSize.X, root.SurfaceSize.Y);
		list.Begin(root.SurfaceSize);
		DrawListData built;
		try
		{
			Walk(list, root, Matrix3x2.Identity, 1f, surfaceRect);
			built = list.End(root.IsHdrSurface);
		}
		catch
		{
			// The walk cleaned the visuals it reached, and the half-built list must not become the one the next composite
			// compares against: keep the last good list, and composite the whole tree again next frame.
			list.Abandon();
			root.MarkDirty();
			throw;
		}
		built.StampDamage(list.Previous.Revision == 0
			? surfaceRect // first build for this surface → repaint all
			: ComputeDamage(list.Previous, list.Current, root.SurfaceSize).Union(list.Current.TexelDamage()));
		CompositeCount++;
	}

	// The pixel region whose geometry differs between two consecutive draw lists of the same surface — the union of the
	// old and new positions of every vertex whose contents changed (so colour fades, moves, and resizes are all covered;
	// texels that changed under unchanged geometry are the buffers' TexelDamage). A
	// structural change (commands added/removed/reordered, or a different vertex count) or any group-opacity layer
	// expands to the whole surface, since those can affect pixels beyond the literal vertex deltas. The previous list
	// is still intact in the other buffer set, so this compares the two in place.
	private static Rect ComputeDamage(DrawListBuffers previous, DrawListBuffers current, Vector2 surfaceSize)
	{
		var pc = previous.Commands.Span;
		var cc = current.Commands.Span;
		var pv = previous.Vertices.Span;
		var cv = current.Vertices.Span;

		if (pv.Length != cv.Length || pc.Length != cc.Length || !pc.SequenceEqual(cc) || current.HasLayers())
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
					var hasBorder = rc.BorderBrush is not null && rc.BorderThickness > 0;
					if (rc.Fill is { } fill)
					{
						var kind = rc.CornerRadius > 0 ? UIPrimKind.RoundedRect : UIPrimKind.Fill;
						// params = (cornerRadius, borderThickness, sizeX, sizeY) — the layout ui.slang's analytic rounded-box
						// SDF reads (uv is 0..1 across the rect; the shader scales by params.zw to local pixels). Fill ignores it.
						var prm = new Vector4(rc.CornerRadius, 0f, rc.Size.X, rc.Size.Y);
						if (fill is not CompositionSurfaceBrush surface)
							AddPaintedRect(list, fill, rect, matrix, alpha, kind, prm, localClip);
						else if (kind == UIPrimKind.Fill)
							AddSurface(list, surface, rc.Size, matrix, alpha, localClip);
						else
							AddImageShape(list, RectShapes.Of(rc).Fill, surface, rect, matrix, alpha, localClip);
					}
					if (hasBorder)
					{
						if (rc.BorderBrush is CompositionSurfaceBrush surface)
							AddImageShape(list, RectShapes.Of(rc).Border, surface, rect, matrix, alpha, localClip);
						else
							AddPaintedRect(list, rc.BorderBrush!, rect, matrix, alpha, UIPrimKind.Border,
								new Vector4(rc.CornerRadius, rc.BorderThickness, rc.Size.X, rc.Size.Y), localClip);
					}
					break;
				}
			case GlyphRunContent gr:
				EmitGlyphRun(list, gr, matrix, alpha, localClip);
				break;
			case ShapeContent shape:
				EmitShape(list, shape, matrix, alpha, localClip);
				break;
		}
	}

	// A glyph run: every glyph's shadow, then every glyph's outline, then the glyphs, so no glyph's effect lands on a
	// neighbouring glyph. Faded with an effect, the glyphs lie over their effects and would show them through, so the run
	// is drawn into one layer that the fade applies to once, as a shape with both a fill and a stroke is.
	private static void EmitGlyphRun(UIDrawList list, GlyphRunContent run, in Matrix3x2 matrix, float alpha, in Rect clip)
	{
		var grouped = alpha is < 1f and > 0f && run.HasEffects && !clip.IsEmpty;
		if (grouped)
		{
			list.BeginLayer(clip, alpha, Vector4.Zero);
			alpha = 1f;
		}
		if (run.ShadowBrush is { } shadow)
			AddGlyphLayer(list, run, shadow, GlyphLayer.Shadow, matrix, alpha, clip);
		if (run.OutlineBrush is { } outline && run.OutlineThickness > 0f)
			AddGlyphLayer(list, run, outline, GlyphLayer.Outline, matrix, alpha, clip);
		if (run.Brush is { } fill)
			AddGlyphLayer(list, run, fill, GlyphLayer.Fill, matrix, alpha, clip);
		if (grouped)
			list.EndLayer(clip);
	}

	// One layer of a glyph run, painted by a colour or a linear gradient over the run's layout box.
	private static void AddGlyphLayer(UIDrawList list, GlyphRunContent run, CompositionBrush paint, GlyphLayer layer, in Matrix3x2 matrix,
		float alpha, in Rect clip)
	{
		switch (paint)
		{
			case CompositionColorBrush color:
				list.AddGlyphRun(run, color.Color.ToPremultiplied(alpha * color.Opacity), matrix, clip, layer: layer);
				break;
			case CompositionLinearGradientBrush gradient:
				list.AddGlyphRun(run, default, matrix, clip, gradient, GradientLine.For(gradient, run.Size), alpha * gradient.Opacity, layer);
				break;
			case CompositionSurfaceBrush:
				// Glyphs are drawn from the font atlas, and a glyph prim samples one texture, so an image can't paint them.
				// The text foregrounds refuse an image brush where it is set; one put on the content directly draws nothing.
				break;
		}
	}

	// A shape: its fill, then its stroke over it, each tessellated through the content's cache for the scale it is drawn
	// at. Faded with both a fill and a stroke, the two overlap (the stroke's inner half lies on the fill), so they are
	// drawn into one layer that the fade applies to once, as a WinUI shape fades as a whole.
	private static void EmitShape(UIDrawList list, ShapeContent shape, in Matrix3x2 matrix, float alpha, in Rect clip)
	{
		var fill = shape.Fill;
		var stroke = shape.StrokeStyle.Thickness > 0f ? shape.Stroke : null;
		if (shape.Path is not { IsEmpty: false } || (fill is null && stroke is null) || alpha <= 0f || clip.IsEmpty)
			return;

		var scale = TessellationScale.For(matrix);
		var box = shape.Bounds;
		var grouped = alpha < 1f && fill is not null && stroke is not null;
		if (grouped)
		{
			list.BeginLayer(clip, alpha, Vector4.Zero);
			alpha = 1f;
		}
		if (fill is not null)
			AddPaintedVector(list, shape.TessellateFill(scale)!, PathTrim.None, fill, box, matrix, alpha, clip);
		if (stroke is not null)
			AddPaintedVector(list, shape.TessellateStroke(scale)!, shape.Trim, stroke, box, matrix, alpha, clip, shape.StrokeBrushAlongLength);
		if (grouped)
			list.EndLayer(clip);
	}

	// Tessellated geometry painted by a colour, a linear gradient over `box` (or along the stroke's length, with
	// `alongLength`), or an image placed in `box`. A stroke that can overlap itself (more than one segment) painted with
	// one alpha below 1 is drawn opaque inside a layer that carries the alpha, so its joins and crossings blend once and
	// look like the rest of it. Paint whose alpha varies (a gradient whose stops differ in alpha, an image) still blends
	// twice where such a stroke overlaps itself.
	private static void AddPaintedVector(UIDrawList list, TessellatedPath path, PathTrim trim, CompositionBrush paint, in Rect box,
		in Matrix3x2 matrix, float alpha, in Rect clip, bool alongLength = false)
	{
		var overlaps = path.IsStroke && path.SegmentCount > 1;
		switch (paint)
		{
			case CompositionColorBrush color:
				{
					var a = alpha * color.Opacity * color.Color.A;
					if (a <= 0f)
						return;
					if (overlaps && a < 1f)
						list.AddVector(path, trim, matrix, (color.Color with { A = 1f }).ToPremultiplied(), clip, groupOpacity: a);
					else
						list.AddVector(path, trim, matrix, color.Color.ToPremultiplied(alpha * color.Opacity), clip);
					break;
				}
			case CompositionLinearGradientBrush gradient:
				{
					var line = GradientLine.For(gradient, box);
					var opacity = alpha * gradient.Opacity;
					alongLength &= path.IsStroke;
					if (overlaps && UniformStopAlpha(gradient) is { } stopAlpha && stopAlpha * opacity < 1f)
					{
						if (stopAlpha * opacity > 0f)
							list.AddVectorGradient(path, trim, matrix, gradient, line, 1f / stopAlpha, clip, stopAlpha * opacity, alongLength);
					}
					else
						list.AddVectorGradient(path, trim, matrix, gradient, line, opacity, clip, alongLength: alongLength);
					break;
				}
			case CompositionSurfaceBrush surface:
				AddVectorImage(list, path, trim, surface, box, matrix, alpha, clip, overlaps);
				break;
			default:
				throw new NotSupportedException($"The compositor cannot paint a {paint.GetType().Name}.");
		}
	}

	// The alpha every stop of a gradient shares, or null when they differ (or there are none).
	private static float? UniformStopAlpha(CompositionGradientBrush gradient)
	{
		var stops = gradient.ColorStops;
		if (stops.Length == 0)
			return null;
		var a = stops[0].Color.A;
		for (var i = 1; i < stops.Length; i++)
		{
			if (stops[i].Color.A != a)
				return null;
		}
		return a;
	}

	// An image brush over vector geometry: the image placed in `box` by the brush's stretch and alignment, sampled through
	// the Vector prim's texture slot at each point's place in it, and painting only where it lies, as on a rect. The
	// geometry is cut to the image in its own space, so that holds under any transform: a letterboxed image leaves the rest
	// of a fill transparent, and a stroke, whose box is its outline's bounds as a gradient's is, paints only within them. A
	// fill is left uncut where the image reaches the box's edge, so its antialiasing fringe there samples the image's edge.
	private static void AddVectorImage(UIDrawList list, TessellatedPath path, PathTrim trim, CompositionSurfaceBrush surface, in Rect box,
		in Matrix3x2 matrix, float alpha, in Rect clip, bool overlaps)
	{
		var a = alpha * surface.Opacity;
		if (surface.Drawn is not { } source || a <= 0f)
			return;
		var placed = surface.ImageRect(new Vector2(box.Width, box.Height));
		var image = new Rect(box.X + placed.X, box.Y + placed.Y, placed.Width, placed.Height);
		if (image.IsEmpty)
			return;
		var localToUv = Matrix3x2.CreateTranslation(-image.X, -image.Y) * Matrix3x2.CreateScale(1f / image.Width, 1f / image.Height);
		var cutAll = path.IsStroke;
		var keepMin = new Vector2(
			cutAll || image.X > box.X ? MathF.Max(image.X, box.X) : float.NegativeInfinity,
			cutAll || image.Y > box.Y ? MathF.Max(image.Y, box.Y) : float.NegativeInfinity);
		var keepMax = new Vector2(
			cutAll || image.Right < box.Right ? MathF.Min(image.Right, box.Right) : float.PositiveInfinity,
			cutAll || image.Bottom < box.Bottom ? MathF.Min(image.Bottom, box.Bottom) : float.PositiveInfinity);
		var groupOpacity = overlaps && a < 1f ? a : 1f;
		var tint = PremultipliedHdrColor.FromStraight(1f, 1f, 1f, groupOpacity < 1f ? 1f : a);
		if (float.IsInfinity(keepMin.X) && float.IsInfinity(keepMin.Y) && float.IsInfinity(keepMax.X) && float.IsInfinity(keepMax.Y))
			list.AddVector(path, trim, matrix, tint, clip, groupOpacity, source, localToUv);
		else
			list.AddVector(path, trim, matrix, tint, clip, groupOpacity, source, localToUv, keepMin, keepMax);
	}

	// A rect painted by a colour or a linear gradient: one quad for a colour, as many bands as the gradient needs.
	private static void AddPaintedRect(UIDrawList list, CompositionBrush paint, in Rect rect, in Matrix3x2 matrix, float alpha, UIPrimKind kind,
		Vector4 prm, in Rect clip)
	{
		switch (paint)
		{
			case CompositionColorBrush color:
				list.AddRect(rect, matrix, color.Color.ToPremultiplied(alpha * color.Opacity), kind, prm, clip);
				break;
			case CompositionLinearGradientBrush gradient:
				{
					Span<Vector2> corners = [new(rect.X, rect.Y), new(rect.Right, rect.Y), new(rect.Right, rect.Bottom), new(rect.X, rect.Bottom)];
					Span<Vector2> uvs = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
					GradientSplitter.AddPolygon(list, corners, uvs, gradient, GradientLine.For(gradient, new Vector2(rect.Width, rect.Height)),
						alpha * gradient.Opacity, matrix, kind, prm, clip, slot: 0);
					break;
				}
			default:
				throw new NotSupportedException($"The compositor cannot paint a {paint.GetType().Name}.");
		}
	}

	// An image brush over a plain rect: the image placed by the brush's stretch and alignment, cut at the rect's edges by
	// narrowing its uv range. White tint (image unchanged) folded by the subtree opacity — the shader does colour *= texSample.
	private static void AddSurface(UIDrawList list, CompositionSurfaceBrush surface, Vector2 size, in Matrix3x2 matrix, float alpha, in Rect clip)
	{
		if (surface.Drawn is not { } source)
			return;
		var image = surface.ImageRect(size);
		var visible = image;
		Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
		if (image.X < 0f || image.Y < 0f || image.Right > size.X || image.Bottom > size.Y)
		{
			visible = image.Intersect(new Rect(0f, 0f, size.X, size.Y));
			if (visible.IsEmpty)
				return;
			uv0 = new Vector2((visible.X - image.X) / image.Width, (visible.Y - image.Y) / image.Height);
			uv1 = new Vector2((visible.Right - image.X) / image.Width, (visible.Bottom - image.Y) / image.Height);
		}
		list.AddImage(visible, uv0, uv1, matrix, PremultipliedHdrColor.FromStraight(1f, 1f, 1f, alpha * surface.Opacity), source, clip);
	}

	// An image painting a rect's rounded fill or its border, which the rect prims cannot texture (their uv and params
	// carry the rounded box): the outline is filled through the Vector prim, the image placed in the rect as on a plain one.
	private static void AddImageShape(UIDrawList list, ShapeContent outline, CompositionSurfaceBrush surface, in Rect rect, in Matrix3x2 matrix,
		float alpha, in Rect clip)
	{
		if (outline.TessellateFill(TessellationScale.For(matrix)) is { TriangleCount: > 0 } path)
			AddVectorImage(list, path, PathTrim.None, surface, rect, matrix, alpha, clip, overlaps: false);
	}

	/// <summary>
	/// The outlines of a rect's rounded fill and of its border ring (the rect less its inner rounded rect, whose radius is
	/// the corner radius less the thickness, as the rect prims draw it), for the rects an image paints. Kept on the content
	/// as its derived work, with their tessellations, and rebuilt only when the size, radius or thickness change.
	/// </summary>
	private sealed class RectShapes
	{
		public readonly ShapeContent Fill = new() { Path = new CompositionPath() };
		public readonly ShapeContent Border = new() { Path = new CompositionPath() };
		private (Vector2 Size, float Radius, float Thickness) _built = (new Vector2(float.NaN), float.NaN, float.NaN);

		public static RectShapes Of(RectContent rect)
		{
			if (rect.Derived is not RectShapes shapes)
			{
				shapes = new RectShapes();
				rect.SetDerived(shapes);
			}
			shapes.Update(rect.Size, rect.CornerRadius, rect.BorderThickness);
			return shapes;
		}

		private void Update(Vector2 size, float radius, float thickness)
		{
			if (_built == (size, radius, thickness))
				return;
			_built = (size, radius, thickness);
			var outer = new Rect(0f, 0f, size.X, size.Y);
			var inner = new Rect(thickness, thickness, size.X - 2f * thickness, size.Y - 2f * thickness);
			var fill = Fill.Path!;
			fill.Clear();
			fill.AddRoundedRectangle(outer, Circular(outer, radius));
			var ring = Border.Path!;
			ring.Clear();
			ring.FillRule = CompositionFillRule.EvenOdd;
			ring.AddRoundedRectangle(outer, Circular(outer, radius));
			ring.AddRoundedRectangle(inner, Circular(inner, radius - thickness));
		}

		// Circular corners of `radius`, at most half the rect's shorter side, as the rounded-box prims draw them.
		private static Vector2 Circular(in Rect rect, float radius) => new(Math.Clamp(radius, 0f, MathF.Max(0f, MathF.Min(rect.Width, rect.Height) / 2f)));
	}
}
