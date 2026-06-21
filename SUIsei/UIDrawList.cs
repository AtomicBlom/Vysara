using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// A pooled builder for the neutral draw list — the analog of ImGui's <c>ImDrawList</c>. Bakes the composed
/// transform into vertex positions and merges consecutive primitives that share <c>(kind, textureId, clip)</c>
/// into one indexed draw command (atlas-everything + state-run batching).
/// </summary>
internal sealed class UIDrawList
{
	private readonly List<OverlayVertex> _verts = [];
	private readonly List<ushort> _indices = [];
	private readonly List<DrawCommand> _commands = [];
	private readonly Vector2 _surfaceSize;

	// Open run state — extended while (kind, tex, clip) match; flushed into a command otherwise.
	private bool _open;
	private UIPrimKind _runKind;
	private int _runTex;
	private Rect _runClip;
	private int _runIdxStart;
	private int _runIdxCount;

	public UIDrawList(Vector2 surfaceSize) => _surfaceSize = surfaceSize;

	/// <summary>Append a transformed quad (the corners are baked through <paramref name="matrix"/>).</summary>
	public void AddRect(in Rect local, in Matrix3x2 matrix, PremultipliedHdrColor color, UIPrimKind kind, Vector4 @params, in Rect clip, int textureId = 0)
	{
		if (clip.IsEmpty)
			return;
		EnsureRun(kind, textureId, clip);

		var baseV = (ushort)_verts.Count;
		AddVertex(new Vector2(local.X, local.Y), new Vector2(0, 0), matrix, color, @params);
		AddVertex(new Vector2(local.Right, local.Y), new Vector2(1, 0), matrix, color, @params);
		AddVertex(new Vector2(local.Right, local.Bottom), new Vector2(1, 1), matrix, color, @params);
		AddVertex(new Vector2(local.X, local.Bottom), new Vector2(0, 1), matrix, color, @params);

		_indices.Add(baseV); _indices.Add((ushort)(baseV + 1)); _indices.Add((ushort)(baseV + 2));
		_indices.Add(baseV); _indices.Add((ushort)(baseV + 2)); _indices.Add((ushort)(baseV + 3));
		_runIdxCount += 6;
	}

	/// <summary>
	/// Lay out a text run as MTSDF glyph quads (PlanningUI.md §D3): advance-based LTR, no shaping (MU5). The run's
	/// local origin <c>(0,0)</c> is the text block's top-left; the baseline drops by the ascent so the first line's
	/// ascenders sit at the top. Each visible glyph emits a quad with its atlas UV and the shared per-run
	/// <c>screenPxRange</c> in <see cref="OverlayVertex.Custom"/>.x; all glyphs share <c>(Glyph, atlas, clip)</c> so they
	/// batch into one indexed draw.
	/// </summary>
	public void AddGlyphRun(string text, float fontSizePx, PremultipliedHdrColor color, FontAtlas font, in Matrix3x2 matrix, in Rect clip, int textureId = 1)
	{
		if (clip.IsEmpty || string.IsNullOrEmpty(text))
			return;
		EnsureRun(UIPrimKind.Glyph, textureId, clip);

		var @params = new Vector4(font.ScreenPxRange(fontSizePx), 0f, 0f, 0f);
		var penX = 0f;
		var baselineY = -font.Ascender * fontSizePx; // ascender is negative (y-down), so the top sits near y=0

		foreach (var ch in text)
		{
			if (font.TryGetGlyph(ch, out var g) && g.HasGeometry)
			{
				var x0 = penX + g.PlaneLeft * fontSizePx;
				var x1 = penX + g.PlaneRight * fontSizePx;
				var y0 = baselineY + g.PlaneTop * fontSizePx;
				var y1 = baselineY + g.PlaneBottom * fontSizePx;

				var baseV = (ushort)_verts.Count;
				AddVertex(new Vector2(x0, y0), new Vector2(g.U0, g.V0), matrix, color, @params);
				AddVertex(new Vector2(x1, y0), new Vector2(g.U1, g.V0), matrix, color, @params);
				AddVertex(new Vector2(x1, y1), new Vector2(g.U1, g.V1), matrix, color, @params);
				AddVertex(new Vector2(x0, y1), new Vector2(g.U0, g.V1), matrix, color, @params);

				_indices.Add(baseV); _indices.Add((ushort)(baseV + 1)); _indices.Add((ushort)(baseV + 2));
				_indices.Add(baseV); _indices.Add((ushort)(baseV + 2)); _indices.Add((ushort)(baseV + 3));
				_runIdxCount += 6;
			}

			penX += font.Advance(ch) * fontSizePx;
		}
	}

	/// <summary>
	/// Open a render-to-texture group layer covering <paramref name="layerRect"/> (surface pixels). The backend
	/// redirects subsequent draws into a transient target until the matching <see cref="EndLayer"/>. A zero-geometry
	/// marker command whose <c>ClipRect</c> is the layer bounds; its degenerate quad (4 vertices, no indices) carries
	/// the premultiplied fade tint <c>(a,a,a,a)</c> the composite-back multiplies the layer by, plus the
	/// <paramref name="effect"/> id+params, so a backend (and the software rasterizer) reads them off the first vertex.
	/// </summary>
	public void BeginLayer(in Rect layerRect, float groupAlpha, Vector4 effect)
	{
		// A marker is its own command — flush the open run so it can't be merged into a preceding primitive run.
		Flush();
		var tint = PremultipliedHdrColor.FromStraight(1f, 1f, 1f, groupAlpha);
		var baseV = (ushort)_verts.Count;
		_verts.Add(new OverlayVertex(new Vector2(layerRect.X, layerRect.Y), new Vector2(0, 0), tint, effect));
		_verts.Add(new OverlayVertex(new Vector2(layerRect.Right, layerRect.Y), new Vector2(1, 0), tint, effect));
		_verts.Add(new OverlayVertex(new Vector2(layerRect.Right, layerRect.Bottom), new Vector2(1, 1), tint, effect));
		_verts.Add(new OverlayVertex(new Vector2(layerRect.X, layerRect.Bottom), new Vector2(0, 1), tint, effect));
		_commands.Add(new DrawCommand(layerRect, 0, _indices.Count, 0, baseV, (uint)UIPrimKind.BeginLayer));
	}

	/// <summary>Close the most-recently-opened layer; the backend composites it back into the parent at the group's
	/// fade alpha. A zero-geometry marker scissored by <paramref name="clip"/> (the parent's clip).</summary>
	public void EndLayer(in Rect clip)
	{
		Flush();
		_commands.Add(new DrawCommand(clip, 0, _indices.Count, 0, 0, (uint)UIPrimKind.EndLayer));
	}

	private void AddVertex(Vector2 local, Vector2 uv, in Matrix3x2 matrix, PremultipliedHdrColor color, Vector4 @params) =>
		_verts.Add(new OverlayVertex(Vector2.Transform(local, matrix), uv, color, @params));

	private void EnsureRun(UIPrimKind kind, int tex, in Rect clip)
	{
		if (_open && _runKind == kind && _runTex == tex && _runClip.Equals(clip))
			return;
		Flush();
		_open = true;
		_runKind = kind;
		_runTex = tex;
		_runClip = clip;
		_runIdxStart = _indices.Count;
		_runIdxCount = 0;
	}

	private void Flush()
	{
		// The compositor's primitive kind becomes the command's neutral Tag; the atlas/source id its texture index.
		if (_open && _runIdxCount > 0)
			_commands.Add(new DrawCommand(_runClip, _runTex, _runIdxStart, _runIdxCount, 0, (uint)_runKind));
		_open = false;
	}

	public DrawListData Build(bool hdr, uint revision)
	{
		Flush();
		return new DrawListData
		{
			Vertices = _verts.ToArray(),
			Indices = _indices.ToArray(),
			Commands = _commands.ToArray(),
			CoordinateSize = _surfaceSize,
			IsHdrTarget = hdr,
			Revision = revision,
		};
	}
}
