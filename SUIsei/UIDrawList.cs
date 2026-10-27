using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// A surface's retained draw-list builder — the analog of ImGui's <c>ImDrawList</c>. Bakes the composed transform
/// into vertex positions and merges consecutive primitives that share <c>(kind, texture slot, clip)</c> into one indexed
/// draw command (atlas-everything + state-run batching).
/// <para>
/// It keeps two <see cref="DrawListBuffers"/> sets and fills them in turn, so the list it built last stays readable
/// (for damage, and for a host still holding it) while the next one builds, and a surface stops allocating once both
/// sets have grown to its size. Each list is handed out through a ring of reused <see cref="DrawListData"/> views.
/// </para>
/// <para>
/// Indices are 16-bit and relative to a window of at most <see cref="MaxWindowVertices"/> vertices, which each command
/// names by its <see cref="DrawCommand.VertexOffset"/>. When a primitive would not fit in the current window, the open
/// run is flushed at that primitive boundary and continues in a new window starting at the primitive's first vertex,
/// so a surface can hold any number of vertices.
/// </para>
/// </summary>
internal sealed class UIDrawList
{
	/// <summary>
	/// The vertices one 16-bit index window holds, addressed by indices 0..65,534. Index 0xFFFF is never written,
	/// because some graphics APIs treat it as the primitive-restart value even for triangle lists.
	/// </summary>
	internal const int MaxWindowVertices = ushort.MaxValue;

	// A view's buffer set is refilled two composites after the view was bound, and the view itself is rebound
	// ViewCount composites after; in between, reading it throws instead of quietly returning a newer list.
	private const int ViewCount = 4;

	private readonly DrawListBuffers[] _sets = [new(), new()];
	private readonly DrawListData[] _views = [new(), new(), new(), new()];
	private readonly List<ShapedGlyph> _glyphBuf = [];
	private DrawListBuffers _set; // the set this composite fills
	private Vector2 _surfaceSize;

	// Each texture source's slot in the list being built: one lookup per primitive, so a list with many images or
	// pages assigns its slots without searching the table. Cleared per composite, keeping its storage.
	private readonly Dictionary<TextureSource, int> _slotOf = new(ReferenceEqualityComparer.Instance);

	// The surface's texture revision: moves when a list's slot table differs from the one before it, or when a source
	// in the current list changes its texels.
	private uint _textureRevision;

	// The first vertex of the current index window: every command emitted in it carries this as its VertexOffset.
	private int _windowStart;

	// Group layers opened and not yet closed, innermost last: each grows the bounds of what is drawn inside it, which
	// becomes its rect when it closes.
	private readonly RetainedBuffer<OpenLayer> _layers = new(4);

	private struct OpenLayer
	{
		public int Command;     // its BeginLayer command
		public int Marker;      // the first of its marker's four vertices
		public Rect Clip;       // what the group is clipped to
		public Vector2 Min, Max;
		public bool Drawn;      // whether anything was drawn inside
	}

	// Open run state — extended while (kind, tex, clip) match; flushed into a command otherwise.
	private bool _open;
	private UIPrimKind _runKind;
	private int _runTex;
	private Rect _runClip;
	private int _runIdxStart;
	private int _runIdxCount;

	public UIDrawList() => _set = _sets[0];

	/// <summary>The revision of the latest composite: 1 for the first, then one more per <see cref="Begin"/>.</summary>
	public uint Revision { get; private set; }

	/// <summary>The list the latest <see cref="End"/> handed out, or <see cref="DrawListData.Empty"/> before the first.</summary>
	public DrawListData Last { get; private set; } = DrawListData.Empty;

	/// <summary>The set the latest composite filled.</summary>
	public DrawListBuffers Current => _set;

	/// <summary>The set the composite before it filled; its <see cref="DrawListBuffers.Revision"/> is 0 on the first.</summary>
	public DrawListBuffers Previous => _sets[(Revision + 1) & 1];

	/// <summary>Start the next composite: empty the set the composite before last filled, keeping its storage.</summary>
	public void Begin(Vector2 surfaceSize)
	{
		Revision++;
		_set = _sets[Revision & 1];
		_set.Reset(Revision);
		_surfaceSize = surfaceSize;
		_windowStart = 0;
		_open = false;
		_layers.Clear();
		_slotOf.Clear();
		SlotFor(TextureSource.White); // slot 0, what untextured commands sample
	}

	/// <summary>Close the composite and hand out its list. Its texture revision moves only when its slot table differs
	/// from the previous list's: different sources or slots, or a source whose texels have changed since.</summary>
	public DrawListData End(bool hdr)
	{
		Flush();
		if (!_set.CompareTextures(Previous) || Previous.Revision == 0)
			_textureRevision++;
		var view = _views[Revision % ViewCount];
		view.Bind(_set, _surfaceSize, hdr, _textureRevision);
		Last = view;
		return view;
	}

	/// <summary>
	/// For a frame that reuses the current list unchanged: move its texture revision if a source it samples has
	/// changed its texels since the list recorded them, so the host re-uploads it although the geometry is the same, and
	/// make the list's damage the pixels drawn with those sources: the whole surface when the list has group layers, whose
	/// composite (an effect's blur, say) can carry a change past what was drawn, as a recomposite's damage does.
	/// Allocation-free; one version comparison per slot.
	/// </summary>
	public void RefreshTextureRevision()
	{
		if (Revision == 0 || !_set.RefreshVersions())
			return;
		_textureRevision++;
		Last.StampTextureRevision(_textureRevision);
		Last.Damage = _set.HasLayers() ? new Rect(0f, 0f, _surfaceSize.X, _surfaceSize.Y) : _set.TexelDamage();
	}

	// The slot `source` holds in the list being built, assigning it the next one on first use, so slots are dense and
	// numbered in the order the walk first samples each source.
	private int SlotFor(TextureSource source)
	{
		if (_slotOf.TryGetValue(source, out var slot))
			return slot;
		slot = _set.Slots.Count;
		_set.Slots.Add(source);
		_set.SlotVersions.Add(source.Version);
		_slotOf.Add(source, slot);
		return slot;
	}

	/// <summary>Append a transformed quad (the corners are baked through <paramref name="matrix"/>), its uv running 0..1
	/// across it.</summary>
	public void AddRect(in Rect local, in Matrix3x2 matrix, PremultipliedHdrColor color, UIPrimKind kind, Vector4 @params, in Rect clip, int slot = 0) =>
		AddQuad(local, Vector2.Zero, Vector2.One, matrix, color, kind, @params, clip, slot);

	/// <summary>Append a tinted image quad sampling the part of <paramref name="source"/> from <paramref name="uv0"/> to
	/// <paramref name="uv1"/>, through the source's slot in this list's slot table.</summary>
	public void AddImage(in Rect local, Vector2 uv0, Vector2 uv1, in Matrix3x2 matrix, PremultipliedHdrColor tint, TextureSource source, in Rect clip)
	{
		if (clip.IsEmpty || source.IsDisposed)
			return;
		AddQuad(local, uv0, uv1, matrix, tint, UIPrimKind.Image, Vector4.Zero, clip, SlotFor(source));
	}

	private void AddQuad(in Rect local, Vector2 uv0, Vector2 uv1, in Matrix3x2 matrix, PremultipliedHdrColor color, UIPrimKind kind, Vector4 @params, in Rect clip, int slot)
	{
		if (clip.IsEmpty)
			return;
		EnsureRun(kind, slot, clip);

		var first = ReserveVertices(4);
		AddVertex(new Vector2(local.X, local.Y), new Vector2(uv0.X, uv0.Y), matrix, color, @params);
		AddVertex(new Vector2(local.Right, local.Y), new Vector2(uv1.X, uv0.Y), matrix, color, @params);
		AddVertex(new Vector2(local.Right, local.Bottom), new Vector2(uv1.X, uv1.Y), matrix, color, @params);
		AddVertex(new Vector2(local.X, local.Bottom), new Vector2(uv0.X, uv1.Y), matrix, color, @params);
		AddQuadIndices(first);
	}

	/// <summary>
	/// Append a convex polygon, its corners in order around it, as a fan of triangles from the first corner: each corner
	/// with its own uv and colour (premultiplied linear light, rounded to half precision here), all sharing
	/// <paramref name="params"/>. How a gradient's bands and the glyph pieces it cuts reach the list; a four-corner polygon
	/// is indexed exactly as a quad.
	/// </summary>
	public void AddPolygon(ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> color, in Matrix3x2 matrix,
		UIPrimKind kind, Vector4 @params, in Rect clip, int slot) =>
		AddPolygon(local, uv, color, default, @params, matrix, kind, clip, slot);

	/// <summary>As <see cref="AddPolygon(ReadOnlySpan{Vector2}, ReadOnlySpan{Vector2}, ReadOnlySpan{Vector4}, in Matrix3x2, UIPrimKind, Vector4, in Rect, int)"/>,
	/// with each corner's own <paramref name="params"/>: how the pieces a gradient cuts from a vector triangle keep their
	/// edge distances.</summary>
	public void AddPolygon(ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> color, ReadOnlySpan<Vector4> @params,
		in Matrix3x2 matrix, UIPrimKind kind, in Rect clip, int slot) =>
		AddPolygon(local, uv, color, @params, default, matrix, kind, clip, slot);

	// A polygon whose corners take `perCorner` params when given, else `shared`.
	private void AddPolygon(ReadOnlySpan<Vector2> local, ReadOnlySpan<Vector2> uv, ReadOnlySpan<Vector4> color, ReadOnlySpan<Vector4> perCorner,
		Vector4 shared, in Matrix3x2 matrix, UIPrimKind kind, in Rect clip, int slot)
	{
		if (clip.IsEmpty || local.Length < 3)
			return;
		EnsureRun(kind, slot, clip);

		var first = ReserveVertices(local.Length);
		for (var i = 0; i < local.Length; i++)
		{
			var c = color[i];
			AddVertex(local[i], uv[i], matrix, PremultipliedHdrColor.FromPremultiplied(c.X, c.Y, c.Z, c.W), perCorner.IsEmpty ? shared : perCorner[i]);
		}
		var indices = _set.Indices;
		for (var i = 1; i + 1 < local.Length; i++)
		{
			indices.Add(first);
			indices.Add((ushort)(first + i));
			indices.Add((ushort)(first + i + 1));
		}
		_runIdxCount += 3 * (local.Length - 2);
	}

	/// <summary>
	/// Lay out a text run as MTSDF glyph quads through the atlas's <see cref="FontAtlas.Shaper"/>,
	/// so kerning / ligatures / contextual forms apply when a real shaper is installed (the default is advance-based
	/// LTR). The run's local origin <c>(0,0)</c> is the text block's top-left; the baseline drops by the ascent so the
	/// first line's ascenders sit at the top. Each shaped glyph emits a quad at its EM pen position (scaled by
	/// <paramref name="fontSizePx"/>) with its atlas UV and the shared per-run <c>screenPxRange</c> in
	/// <see cref="OverlayVertex.Custom"/>.x; all glyphs share <c>(Glyph, atlas, clip)</c> so they batch into one draw.
	/// The atlas page is an ordinary texture source: the glyphs sample it through its slot in this list.
	/// <para>
	/// With a <paramref name="gradient"/> the glyphs are painted by it instead of <paramref name="color"/>: each glyph quad
	/// is cut where the gradient's colour stops being linear (<see cref="GradientSplitter"/>), its pieces keeping their
	/// atlas uv, so text takes a gradient exactly, as rectangles do.
	/// </para>
	/// </summary>
	public void AddGlyphRun(string text, float fontSizePx, PremultipliedHdrColor color, FontAtlas font, in Matrix3x2 matrix, in Rect clip,
		CompositionGradientBrush? gradient = null, in GradientLine line = default, float opacity = 1f)
	{
		if (clip.IsEmpty || string.IsNullOrEmpty(text))
			return;
		var slot = SlotFor(font.Image);
		EnsureRun(UIPrimKind.Glyph, slot, clip);

		var @params = new Vector4(font.ScreenPxRange(fontSizePx), 0f, 0f, 0f);
		var baselineY = -font.Ascender * fontSizePx; // ascender is negative (y-down), so the top sits near y=0

		_glyphBuf.Clear();
		font.Shaper.Shape(text, font, _glyphBuf);
		Span<Vector2> corners = stackalloc Vector2[4];
		Span<Vector2> uvs = stackalloc Vector2[4];
		foreach (var sg in _glyphBuf)
		{
			var g = sg.Glyph; // the shaper omits zero-geometry glyphs, so every entry draws a quad
			var penX = (sg.PenXEm + sg.XOffsetEm) * fontSizePx;
			var x0 = penX + g.PlaneLeft * fontSizePx;
			var x1 = penX + g.PlaneRight * fontSizePx;
			var y0 = baselineY + (sg.YOffsetEm + g.PlaneTop) * fontSizePx;
			var y1 = baselineY + (sg.YOffsetEm + g.PlaneBottom) * fontSizePx;

			if (gradient is not null)
			{
				corners[0] = new Vector2(x0, y0); uvs[0] = new Vector2(g.U0, g.V0);
				corners[1] = new Vector2(x1, y0); uvs[1] = new Vector2(g.U1, g.V0);
				corners[2] = new Vector2(x1, y1); uvs[2] = new Vector2(g.U1, g.V1);
				corners[3] = new Vector2(x0, y1); uvs[3] = new Vector2(g.U0, g.V1);
				GradientSplitter.AddPolygon(this, corners, uvs, gradient, line, opacity, matrix, UIPrimKind.Glyph, @params, clip, slot);
				continue;
			}

			var first = ReserveVertices(4);
			AddVertex(new Vector2(x0, y0), new Vector2(g.U0, g.V0), matrix, color, @params);
			AddVertex(new Vector2(x1, y0), new Vector2(g.U1, g.V0), matrix, color, @params);
			AddVertex(new Vector2(x1, y1), new Vector2(g.U1, g.V1), matrix, color, @params);
			AddVertex(new Vector2(x0, y1), new Vector2(g.U0, g.V1), matrix, color, @params);
			AddQuadIndices(first);
		}
	}

	/// <summary>
	/// Open a render-to-texture group layer whose content is clipped to <paramref name="clip"/> (surface pixels). The
	/// backend redirects subsequent draws into a transient target until the matching <see cref="EndLayer"/>. A
	/// zero-geometry marker command whose <c>ClipRect</c> is the layer's rect: when the layer closes, that becomes the
	/// bounds of what was drawn inside it, rounded out to whole pixels and clipped, so a small group costs a small
	/// target. Its degenerate quad (4 vertices, no indices) spans the same rect and carries the premultiplied fade tint
	/// <c>(a,a,a,a)</c> the composite-back multiplies the layer by, plus the <paramref name="effect"/> id+params, so a
	/// backend (and the software rasterizer) reads them off the first vertex. The marker's <c>VertexOffset</c> is the
	/// absolute index of that vertex, not an index-window base.
	/// </summary>
	public void BeginLayer(in Rect clip, float groupAlpha, Vector4 effect)
	{
		// A marker is its own command — flush the open run so it can't be merged into a preceding primitive run.
		Flush();
		var tint = PremultipliedHdrColor.FromStraight(1f, 1f, 1f, groupAlpha);
		var marker = _set.Vertices.Count;
		_set.Vertices.Add(new OverlayVertex(new Vector2(clip.X, clip.Y), new Vector2(0, 0), tint, effect));
		_set.Vertices.Add(new OverlayVertex(new Vector2(clip.Right, clip.Y), new Vector2(1, 0), tint, effect));
		_set.Vertices.Add(new OverlayVertex(new Vector2(clip.Right, clip.Bottom), new Vector2(1, 1), tint, effect));
		_set.Vertices.Add(new OverlayVertex(new Vector2(clip.X, clip.Bottom), new Vector2(0, 1), tint, effect));
		_layers.Add(new OpenLayer { Command = _set.Commands.Count, Marker = marker, Clip = clip });
		_set.Commands.Add(new DrawCommand(clip, 0, _set.Indices.Count, 0, marker, (uint)UIPrimKind.BeginLayer));
	}

	/// <summary>
	/// Close the most-recently-opened layer; the backend composites it back into the parent at the group's fade alpha.
	/// A zero-geometry marker scissored by <paramref name="clip"/> (the parent's clip). The layer's rect shrinks to what
	/// was drawn in it; a layer nothing was drawn in is removed, markers and all.
	/// </summary>
	public void EndLayer(in Rect clip)
	{
		Flush();
		var layer = _layers[_layers.Count - 1];
		_layers.Truncate(_layers.Count - 1);
		if (!layer.Drawn)
		{
			// Only the BeginLayer marker and its quad were added since it opened.
			_set.Commands.Truncate(layer.Command);
			_set.Vertices.Truncate(layer.Marker);
			return;
		}

		var x0 = MathF.Floor(layer.Min.X);
		var y0 = MathF.Floor(layer.Min.Y);
		var rect = layer.Clip.Intersect(new Rect(x0, y0, MathF.Ceiling(layer.Max.X) - x0, MathF.Ceiling(layer.Max.Y) - y0));
		ref var begin = ref _set.Commands[layer.Command];
		begin = begin with { ClipRect = rect };
		var corners = (stackalloc Vector2[] { new(rect.X, rect.Y), new(rect.Right, rect.Y), new(rect.Right, rect.Bottom), new(rect.X, rect.Bottom) });
		for (var i = 0; i < 4; i++)
		{
			ref var v = ref _set.Vertices[layer.Marker + i];
			v = v with { Position = corners[i] };
		}

		// What the parent draws now includes this layer's composite.
		if (_layers.Count > 0 && !rect.IsEmpty)
		{
			Track(new Vector2(rect.X, rect.Y));
			Track(new Vector2(rect.Right, rect.Bottom));
		}
		_set.Commands.Add(new DrawCommand(clip, 0, _set.Indices.Count, 0, 0, (uint)UIPrimKind.EndLayer));
	}

	// Grow the innermost open layer's bounds to take in a vertex drawn inside it.
	private void Track(Vector2 position)
	{
		if (_layers.Count == 0)
			return;
		ref var layer = ref _layers[_layers.Count - 1];
		if (!layer.Drawn)
		{
			layer.Min = layer.Max = position;
			layer.Drawn = true;
			return;
		}
		layer.Min = Vector2.Min(layer.Min, position);
		layer.Max = Vector2.Max(layer.Max, position);
	}

	/// <summary>
	/// Append tessellated vector geometry, trimmed by <paramref name="trim"/> if it is a stroke, transformed by
	/// <paramref name="matrix"/> into surface pixels and drawn as the <see cref="UIPrimKind.Vector"/> prim in
	/// <paramref name="color"/>, multiplied by <paramref name="texture"/> (the white texel when null) sampled at
	/// <paramref name="localToUv"/> of each local position. Large paths split across index windows at triangle
	/// boundaries.
	/// <para>
	/// With <paramref name="groupOpacity"/> below 1 the path is drawn inside a group layer of its own, whose tint carries
	/// that opacity, so its overlaps (a stroke's joins and self-crossings) blend once: drawn opaque inside it, a
	/// translucent stroke's crossings look like the rest of it.
	/// </para>
	/// </summary>
	public void AddVector(TessellatedPath path, PathTrim trim, in Matrix3x2 matrix, PremultipliedHdrColor color, in Rect clip,
		float groupOpacity = 1f, TextureSource? texture = null, Matrix3x2 localToUv = default)
	{
		if (clip.IsEmpty || path.TriangleCount == 0 || groupOpacity <= 0f || texture is { IsDisposed: true })
			return;
		var grouped = groupOpacity < 1f;
		if (grouped)
			BeginLayer(clip, groupOpacity, Vector4.Zero);
		EnsureRun(UIPrimKind.Vector, texture is null ? 0 : SlotFor(texture), clip);
		var sink = new VectorEmitter(this, matrix, color, localToUv);
		path.Emit(ref sink, trim);
		if (grouped)
			EndLayer(clip);
	}

	// Append whole triangles of vector vertices to the open run, starting a new index window between triangles when the
	// current one fills.
	private void AddVectorTriangles(ReadOnlySpan<VectorVertex> vertices, in Matrix3x2 matrix, PremultipliedHdrColor color, in Matrix3x2 localToUv)
	{
		while (vertices.Length > 0)
		{
			var room = (MaxWindowVertices - (_set.Vertices.Count - _windowStart)) / 3 * 3;
			var take = Math.Min(vertices.Length, room >= 3 ? room : MaxWindowVertices / 3 * 3);
			var first = ReserveVertices(take);
			for (var i = 0; i < take; i++)
			{
				ref readonly var v = ref vertices[i];
				var position = Vector2.Transform(v.Position, matrix);
				Track(position);
				_set.Vertices.Add(new OverlayVertex(position, Vector2.Transform(v.Position, localToUv), color, v.Distances));
				_set.Indices.Add((ushort)(first + i));
			}
			_runIdxCount += take;
			vertices = vertices[take..];
		}
	}

	/// <summary>Writes a tessellated path's triangles into the draw list, transformed and coloured.</summary>
	private readonly struct VectorEmitter(UIDrawList list, Matrix3x2 matrix, PremultipliedHdrColor color, Matrix3x2 localToUv) : IVectorSink
	{
		public void AddTriangles(ReadOnlySpan<VectorVertex> vertices) => list.AddVectorTriangles(vertices, matrix, color, localToUv);
	}

	/// <summary>
	/// As <see cref="AddVector(TessellatedPath, PathTrim, in Matrix3x2, PremultipliedHdrColor, in Rect, float, TextureSource?, Matrix3x2)"/>, keeping only what lies in the box from <paramref name="keepMin"/> to
	/// <paramref name="keepMax"/> in the path's local space (a side at an infinity is open): each triangle is cut to it,
	/// its pieces keeping its edge distances, so the box clips exactly under any transform. How an image paints only
	/// where it lies, leaving the rest of a letterboxed shape transparent. The cut edges are not antialiased.
	/// </summary>
	public void AddVector(TessellatedPath path, PathTrim trim, in Matrix3x2 matrix, PremultipliedHdrColor color, in Rect clip, float groupOpacity,
		TextureSource? texture, Matrix3x2 localToUv, Vector2 keepMin, Vector2 keepMax)
	{
		if (clip.IsEmpty || path.TriangleCount == 0 || groupOpacity <= 0f || !(keepMax.X > keepMin.X) || !(keepMax.Y > keepMin.Y)
			|| texture is { IsDisposed: true })
			return;
		var grouped = groupOpacity < 1f;
		if (grouped)
			BeginLayer(clip, groupOpacity, Vector4.Zero);
		var slot = texture is null ? 0 : SlotFor(texture);
		var sink = new KeptVectorEmitter(this, matrix, color.ToVector4(), localToUv, keepMin, keepMax, clip, slot);
		path.Emit(ref sink, trim);
		if (grouped)
			EndLayer(clip);
	}

	/// <summary>Writes a tessellated path's triangles into the draw list, each cut to a box.</summary>
	private readonly struct KeptVectorEmitter(UIDrawList list, Matrix3x2 matrix, Vector4 color, Matrix3x2 localToUv, Vector2 keepMin, Vector2 keepMax,
		Rect clip, int slot) : IVectorSink
	{
		public void AddTriangles(ReadOnlySpan<VectorVertex> vertices)
		{
			Span<Vector2> corner = stackalloc Vector2[3];
			Span<Vector4> distances = stackalloc Vector4[3];
			Span<Vector2> keptPos = stackalloc Vector2[8];
			Span<Vector4> keptDistances = stackalloc Vector4[8];
			Span<Vector2> keptUv = stackalloc Vector2[8];
			Span<Vector4> colors = stackalloc Vector4[8];
			colors.Fill(color);
			for (var i = 0; i + 2 < vertices.Length; i += 3)
			{
				for (var k = 0; k < 3; k++)
				{
					corner[k] = vertices[i + k].Position;
					distances[k] = vertices[i + k].Distances;
				}
				var m = GradientSplitter.CutToBox(corner, distances, keepMin, keepMax, keptPos, keptDistances);
				if (m < 3)
					continue;
				for (var k = 0; k < m; k++)
					keptUv[k] = Vector2.Transform(keptPos[k], localToUv);
				list.AddPolygon(keptPos[..m], keptUv[..m], colors[..m], keptDistances[..m], matrix, UIPrimKind.Vector, clip, slot);
			}
		}
	}

	/// <summary>
	/// Append tessellated vector geometry, trimmed by <paramref name="trim"/> if it is a stroke, painted by
	/// <paramref name="gradient"/> along <paramref name="line"/> through the path's local space with its alpha multiplied
	/// by <paramref name="opacity"/>. Each triangle is cut where the gradient's colour stops being linear across it and the
	/// pieces' corners take the gradient's colours (<see cref="GradientSplitter"/>), so the gradient is exact, colours above
	/// paper white included, and the pieces keep the triangle's edge distances, so its antialiasing is unchanged.
	/// <paramref name="groupOpacity"/> is as for <see cref="AddVector(TessellatedPath, PathTrim, in Matrix3x2, PremultipliedHdrColor, in Rect, float, TextureSource?, Matrix3x2)"/>.
	/// <para>
	/// With <paramref name="alongLength"/> a stroke's gradient runs along it instead of along <paramref name="line"/>: each
	/// vertex is measured by its arc length over the stroke's untrimmed <see cref="TessellatedPath.Length"/>, which is linear
	/// across every triangle, so the cut lands exactly on each stop's offset along the length.
	/// </para>
	/// </summary>
	public void AddVectorGradient(TessellatedPath path, PathTrim trim, in Matrix3x2 matrix, CompositionGradientBrush gradient, in GradientLine line,
		float opacity, in Rect clip, float groupOpacity = 1f, bool alongLength = false)
	{
		if (clip.IsEmpty || path.TriangleCount == 0 || groupOpacity <= 0f || gradient.ColorStops.Length == 0)
			return;
		var grouped = groupOpacity < 1f;
		if (grouped)
			BeginLayer(clip, groupOpacity, Vector4.Zero);
		var length = alongLength && path.IsStroke ? MathF.Max(path.Length, float.Epsilon) : 0f;
		var sink = new GradientVectorEmitter(this, matrix, gradient, line, length, opacity, clip);
		path.Emit(ref sink, trim);
		if (grouped)
			EndLayer(clip);
	}

	/// <summary>Writes a tessellated path's triangles into the draw list, each cut into the gradient's bands: measured along
	/// the line, or along the stroke over <c>length</c> when it is above 0.</summary>
	private readonly struct GradientVectorEmitter(UIDrawList list, Matrix3x2 matrix, CompositionGradientBrush gradient, GradientLine line,
		float length, float opacity, Rect clip) : IVectorSink
	{
		public void AddTriangles(ReadOnlySpan<VectorVertex> vertices)
		{
			Span<Vector2> position = stackalloc Vector2[3];
			Span<Vector2> uv = stackalloc Vector2[3]; // slot 0 is one white texel: any uv samples it
			Span<Vector4> distances = stackalloc Vector4[3];
			Span<float> t = stackalloc float[3];
			var degenerate = length <= 0f && line.IsDegenerate;
			for (var i = 0; i + 2 < vertices.Length; i += 3)
			{
				for (var k = 0; k < 3; k++)
				{
					ref readonly var v = ref vertices[i + k];
					position[k] = v.Position;
					distances[k] = v.Distances;
					t[k] = length > 0f ? v.ArcLength / length : degenerate ? 0f : line.T(v.Position);
				}
				GradientSplitter.AddVectorTriangle(list, position, uv, distances, t, degenerate, gradient, opacity, matrix, clip, slot: 0);
			}
		}
	}

	private void AddVertex(Vector2 local, Vector2 uv, in Matrix3x2 matrix, PremultipliedHdrColor color, Vector4 @params)
	{
		var position = Vector2.Transform(local, matrix);
		Track(position);
		_set.Vertices.Add(new OverlayVertex(position, uv, color, @params));
	}

	// The two triangles of a quad whose four vertices start at window index `first`.
	private void AddQuadIndices(ushort first)
	{
		var indices = _set.Indices;
		indices.Add(first);
		indices.Add((ushort)(first + 1));
		indices.Add((ushort)(first + 2));
		indices.Add(first);
		indices.Add((ushort)(first + 2));
		indices.Add((ushort)(first + 3));
		_runIdxCount += 6;
	}

	// Make room for a primitive of `count` vertices, all of which its indices address, and return the window index
	// its first vertex takes. When it would not fit in the current window, the open run is flushed here, between
	// primitives, and reopened with the same state in a new window that starts at this primitive.
	private ushort ReserveVertices(int count)
	{
		var used = _set.Vertices.Count - _windowStart;
		if (used + count <= MaxWindowVertices)
			return (ushort)used;

		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxWindowVertices);
		var reopen = _open;
		Flush();
		_windowStart = _set.Vertices.Count;
		if (reopen)
			OpenRun(_runKind, _runTex, _runClip);
		return 0;
	}

	private void EnsureRun(UIPrimKind kind, int tex, in Rect clip)
	{
		if (_open && _runKind == kind && _runTex == tex && _runClip.Equals(clip))
			return;
		Flush();
		OpenRun(kind, tex, clip);
	}

	private void OpenRun(UIPrimKind kind, int tex, in Rect clip)
	{
		_open = true;
		_runKind = kind;
		_runTex = tex;
		_runClip = clip;
		_runIdxStart = _set.Indices.Count;
		_runIdxCount = 0;
	}

	private void Flush()
	{
		// The compositor's primitive kind becomes the command's neutral Tag; the atlas/source id its texture index.
		if (_open && _runIdxCount > 0)
			_set.Commands.Add(new DrawCommand(_runClip, _runTex, _runIdxStart, _runIdxCount, _windowStart, (uint)_runKind));
		_open = false;
	}
}
