using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// A surface's retained draw-list builder — the analog of ImGui's <c>ImDrawList</c>. Bakes the composed transform
/// into vertex positions and merges consecutive primitives that share <c>(kind, textureId, clip)</c> into one indexed
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
		_slotOf.Clear();
		SlotFor(TextureSource.White); // slot 0, what untextured commands sample
	}

	/// <summary>Close the composite and hand out its list. Its texture revision moves only when its slot table differs
	/// from the previous list's: different sources or slots, or a source whose texels have changed since.</summary>
	public DrawListData End(bool hdr)
	{
		Flush();
		if (Previous.Revision == 0 || !_set.SameTextures(Previous))
			_textureRevision++;
		var view = _views[Revision % ViewCount];
		view.Bind(_set, _surfaceSize, hdr, _textureRevision);
		Last = view;
		return view;
	}

	/// <summary>
	/// For a frame that reuses the current list unchanged: move its texture revision if a source it samples has
	/// changed its texels since the list recorded them, so the host re-uploads it although the geometry is the same.
	/// Allocation-free; one version comparison per slot.
	/// </summary>
	public void RefreshTextureRevision()
	{
		if (Revision == 0 || !_set.RefreshVersions())
			return;
		_textureRevision++;
		Last.StampTextureRevision(_textureRevision);
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

	/// <summary>Append a transformed quad (the corners are baked through <paramref name="matrix"/>).</summary>
	public void AddRect(in Rect local, in Matrix3x2 matrix, PremultipliedHdrColor color, UIPrimKind kind, Vector4 @params, in Rect clip, int textureId = 0)
	{
		if (clip.IsEmpty)
			return;
		EnsureRun(kind, textureId, clip);

		var first = ReserveVertices(4);
		AddVertex(new Vector2(local.X, local.Y), new Vector2(0, 0), matrix, color, @params);
		AddVertex(new Vector2(local.Right, local.Y), new Vector2(1, 0), matrix, color, @params);
		AddVertex(new Vector2(local.Right, local.Bottom), new Vector2(1, 1), matrix, color, @params);
		AddVertex(new Vector2(local.X, local.Bottom), new Vector2(0, 1), matrix, color, @params);
		AddQuadIndices(first);
	}

	/// <summary>Append a tinted image quad sampling <paramref name="source"/> across its full UV, through the source's
	/// slot in this list's slot table.</summary>
	public void AddImage(in Rect local, in Matrix3x2 matrix, PremultipliedHdrColor tint, ImageSource source, in Rect clip)
	{
		if (clip.IsEmpty)
			return;
		AddRect(local, matrix, tint, UIPrimKind.Image, Vector4.Zero, clip, SlotFor(source));
	}

	/// <summary>
	/// Lay out a text run as MTSDF glyph quads through the atlas's <see cref="FontAtlas.Shaper"/>,
	/// so kerning / ligatures / contextual forms apply when a real shaper is installed (the default is advance-based
	/// LTR). The run's local origin <c>(0,0)</c> is the text block's top-left; the baseline drops by the ascent so the
	/// first line's ascenders sit at the top. Each shaped glyph emits a quad at its EM pen position (scaled by
	/// <paramref name="fontSizePx"/>) with its atlas UV and the shared per-run <c>screenPxRange</c> in
	/// <see cref="OverlayVertex.Custom"/>.x; all glyphs share <c>(Glyph, atlas, clip)</c> so they batch into one draw.
	/// The atlas page is an ordinary texture source: the glyphs sample it through its slot in this list.
	/// </summary>
	public void AddGlyphRun(string text, float fontSizePx, PremultipliedHdrColor color, FontAtlas font, in Matrix3x2 matrix, in Rect clip)
	{
		if (clip.IsEmpty || string.IsNullOrEmpty(text))
			return;
		EnsureRun(UIPrimKind.Glyph, SlotFor(font.Image), clip);

		var @params = new Vector4(font.ScreenPxRange(fontSizePx), 0f, 0f, 0f);
		var baselineY = -font.Ascender * fontSizePx; // ascender is negative (y-down), so the top sits near y=0

		_glyphBuf.Clear();
		font.Shaper.Shape(text, font, _glyphBuf);
		foreach (var sg in _glyphBuf)
		{
			var g = sg.Glyph; // the shaper omits zero-geometry glyphs, so every entry draws a quad
			var penX = (sg.PenXEm + sg.XOffsetEm) * fontSizePx;
			var x0 = penX + g.PlaneLeft * fontSizePx;
			var x1 = penX + g.PlaneRight * fontSizePx;
			var y0 = baselineY + (sg.YOffsetEm + g.PlaneTop) * fontSizePx;
			var y1 = baselineY + (sg.YOffsetEm + g.PlaneBottom) * fontSizePx;

			var first = ReserveVertices(4);
			AddVertex(new Vector2(x0, y0), new Vector2(g.U0, g.V0), matrix, color, @params);
			AddVertex(new Vector2(x1, y0), new Vector2(g.U1, g.V0), matrix, color, @params);
			AddVertex(new Vector2(x1, y1), new Vector2(g.U1, g.V1), matrix, color, @params);
			AddVertex(new Vector2(x0, y1), new Vector2(g.U0, g.V1), matrix, color, @params);
			AddQuadIndices(first);
		}
	}

	/// <summary>
	/// Open a render-to-texture group layer covering <paramref name="layerRect"/> (surface pixels). The backend
	/// redirects subsequent draws into a transient target until the matching <see cref="EndLayer"/>. A zero-geometry
	/// marker command whose <c>ClipRect</c> is the layer bounds; its degenerate quad (4 vertices, no indices) carries
	/// the premultiplied fade tint <c>(a,a,a,a)</c> the composite-back multiplies the layer by, plus the
	/// <paramref name="effect"/> id+params, so a backend (and the software rasterizer) reads them off the first vertex.
	/// The marker's <c>VertexOffset</c> is the absolute index of that vertex, not an index-window base.
	/// </summary>
	public void BeginLayer(in Rect layerRect, float groupAlpha, Vector4 effect)
	{
		// A marker is its own command — flush the open run so it can't be merged into a preceding primitive run.
		Flush();
		var tint = PremultipliedHdrColor.FromStraight(1f, 1f, 1f, groupAlpha);
		var marker = _set.Vertices.Count;
		_set.Vertices.Add(new OverlayVertex(new Vector2(layerRect.X, layerRect.Y), new Vector2(0, 0), tint, effect));
		_set.Vertices.Add(new OverlayVertex(new Vector2(layerRect.Right, layerRect.Y), new Vector2(1, 0), tint, effect));
		_set.Vertices.Add(new OverlayVertex(new Vector2(layerRect.Right, layerRect.Bottom), new Vector2(1, 1), tint, effect));
		_set.Vertices.Add(new OverlayVertex(new Vector2(layerRect.X, layerRect.Bottom), new Vector2(0, 1), tint, effect));
		_set.Commands.Add(new DrawCommand(layerRect, 0, _set.Indices.Count, 0, marker, (uint)UIPrimKind.BeginLayer));
	}

	/// <summary>Close the most-recently-opened layer; the backend composites it back into the parent at the group's
	/// fade alpha. A zero-geometry marker scissored by <paramref name="clip"/> (the parent's clip).</summary>
	public void EndLayer(in Rect clip)
	{
		Flush();
		_set.Commands.Add(new DrawCommand(clip, 0, _set.Indices.Count, 0, 0, (uint)UIPrimKind.EndLayer));
	}

	private void AddVertex(Vector2 local, Vector2 uv, in Matrix3x2 matrix, PremultipliedHdrColor color, Vector4 @params) =>
		_set.Vertices.Add(new OverlayVertex(Vector2.Transform(local, matrix), uv, color, @params));

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
