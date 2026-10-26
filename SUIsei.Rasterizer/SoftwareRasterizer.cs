using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SUIsei.Drawing;

namespace SUIsei.Rasterizer;

/// <summary>
/// A CPU reference rasterizer for the neutral <see cref="DrawListData"/> — the software twin of the engine's
/// <c>ui.slang</c> fragment shader (Loom.Shaders/Shaders/ui.slang). It walks the draw commands, rasterizes each
/// triangle, and shades per pixel with the same analytic math the GPU ran: rounded-box SDFs for rect/border, MTSDF
/// coverage for glyphs, and signed edge distances for vector geometry, each antialiased as its distance over its
/// screen-space gradient's length. Compositing is premultiplied alpha-over in <b>linear</b> space (the GPU rendered to a
/// linear target and re-encoded sRGB on store), so <see cref="ResolveToRgba8"/> does that sRGB encode itself. Pixels are
/// sampled at their centres with the GPU's top-left fill rule, so a centre on an edge two triangles share (a quad's
/// diagonal, the cut between a gradient's bands) is drawn once, never twice or not at all.
///
/// Covers all six drawable kinds: <see cref="UIPrimKind.Fill"/>/<see cref="UIPrimKind.RoundedRect"/>/<see cref="UIPrimKind.Border"/>
/// (analytic), <see cref="UIPrimKind.Glyph"/> (MTSDF), <see cref="UIPrimKind.Image"/> (tinted blit) and
/// <see cref="UIPrimKind.Vector"/> (edge distances times a texel), the last three sampling the texture in their
/// <c>TextureIndex</c> slot of the list's <see cref="DrawListData.TextureSlots"/>. Such a command whose slot the list
/// lacks is skipped (counted in <see cref="SkippedCommands"/>). It also honours the
/// <see cref="UIPrimKind.BeginLayer"/>/<see cref="UIPrimKind.EndLayer"/> group-opacity markers by rendering the
/// bracketed subtree into an offscreen framebuffer and compositing it back at the group's fade alpha — the CPU twin
/// of the backend RT stack, so it gives true (non-double-blended) group opacity headlessly. A command whose tag is
/// none of these kinds throws <see cref="ArgumentException"/> before anything is drawn, rather than being shaded as
/// something it is not.
/// </summary>
public sealed class SoftwareRasterizer
{
	private readonly int _width;
	private readonly int _height;

	// Framebuffer in LINEAR, PREMULTIPLIED RGBA — the space the GPU blended in. Resolved to sRGB bytes on demand.
	private readonly Vector4[] _fb;

	// Open render-to-texture group layers (BeginLayer/EndLayer). Draws target the top layer's framebuffer, clipped to
	// the layer's rect (its BeginLayer marker's ClipRect, as a backend's render area is); on EndLayer the rect is
	// composited back into its parent at the group's fade alpha — the CPU mirror of the backend RT stack. A layer's
	// framebuffer is surface-sized (children render at their natural surface positions) but only its rect is cleared and
	// composited. Framebuffers are pooled by nesting depth, so rendering layers allocates nothing once warm.
	private readonly Stack<(Vector4[] Fb, float Alpha, PixelRect Rect)> _layers = new();
	private readonly List<Vector4[]> _layerPool = [];

	/// <summary>An inclusive pixel rectangle.</summary>
	private readonly record struct PixelRect(int X0, int Y0, int X1, int Y1)
	{
		public bool IsEmpty => X0 > X1 || Y0 > Y1;
	}

	// The textures of the list being drawn, by slot: its TextureSlots resolved through _textureCache. Rebound at the
	// start of each Render, so a command's TextureIndex always means a slot of the list it came from.
	private Texture?[] _slots = [];
	private int _slotCount;

	// Each source's texels copied into a Texture once per Version, kept across lists and frames.
	private readonly Dictionary<TextureSource, (uint Version, Texture Texture)> _textureCache = new(ReferenceEqualityComparer.Instance);

	public SoftwareRasterizer(int width, int height)
	{
		_width = width;
		_height = height;
		_fb = new Vector4[width * height];
	}

	public int Width => _width;
	public int Height => _height;

	/// <summary>The framebuffer's value at pixel (<paramref name="x"/>, <paramref name="y"/>): linear light, premultiplied,
	/// before any clamp, so colours above paper white read as drawn.</summary>
	public Vector4 ReadLinear(int x, int y) => _fb[y * _width + x];

	/// <summary>Triangles rasterized by the last <see cref="Render"/> call (perf-shape instrumentation).</summary>
	public int TrianglesDrawn { get; private set; }

	/// <summary>Glyph/Image commands skipped in the last <see cref="Render"/> call because their slot was missing.</summary>
	public int SkippedCommands { get; private set; }

	/// <summary>Group-opacity layers composited back in the last <see cref="Render"/> call (EndLayer markers).</summary>
	public int LayersComposited { get; private set; }

	// The framebuffer draws currently target: the open layer on top of the stack, else the base framebuffer.
	private Vector4[] ActiveTarget => _layers.Count > 0 ? _layers.Peek().Fb : _fb;

	// Bind a list's slot table: slot i samples the texels of slots[i], copied once per source and version, as a host
	// uploads each source once and replaces it when its version moves.
	private void BindSlots(IReadOnlyList<TextureSource> slots)
	{
		if (_slots.Length < slots.Count)
			_slots = new Texture?[slots.Count];
		_slotCount = slots.Count;
		for (var i = 0; i < slots.Count; i++)
		{
			var source = slots[i];
			if (!_textureCache.TryGetValue(source, out var cached) || cached.Version != source.Version)
			{
				cached = (source.Version, Texture.FromSource(source));
				_textureCache[source] = cached;
			}
			_slots[i] = cached.Texture;
		}
	}

	private PixelRect FullSurface => new(0, 0, _width - 1, _height - 1);

	// The pixels whose centres a rect can cover: inclusive integer bounds, clamped to the framebuffer.
	private PixelRect PixelBounds(in Rect r) => new(
		Math.Max(0, (int)MathF.Floor(r.X)),
		Math.Max(0, (int)MathF.Floor(r.Y)),
		Math.Min(_width - 1, (int)MathF.Ceiling(r.Right) - 1),
		Math.Min(_height - 1, (int)MathF.Ceiling(r.Bottom) - 1));

	private static PixelRect Intersect(PixelRect a, PixelRect b) =>
		new(Math.Max(a.X0, b.X0), Math.Max(a.Y0, b.Y0), Math.Min(a.X1, b.X1), Math.Min(a.Y1, b.Y1));

	// The pooled framebuffer for a layer opened at nesting `depth`.
	private Vector4[] RentLayer(int depth)
	{
		while (_layerPool.Count <= depth)
			_layerPool.Add(new Vector4[_width * _height]);
		return _layerPool[depth];
	}

	private bool TryGetSlot(int index, out Texture? texture)
	{
		texture = (uint)index < (uint)_slotCount ? _slots[index] : null;
		return texture is not null;
	}

	/// <summary>Fill the whole framebuffer with one linear-premultiplied colour (the cleared backbuffer). Chunked
	/// across threads for large surfaces — a constant fill, so the result is identical to a serial fill.</summary>
	public void Clear(Vector4 linearPremultiplied)
	{
		var fb = _fb;
		var n = fb.Length;
		if (n < 1 << 18) // small (≲512²) surfaces aren't worth the parallel overhead
		{
			Array.Fill(fb, linearPremultiplied);
			return;
		}

		var workers = Math.Min(Environment.ProcessorCount, n >> 16); // ≥64k elements per worker
		var chunk = (n + workers - 1) / workers;
		Parallel.For(0, workers, w =>
		{
			var start = w * chunk;
			var count = Math.Min(chunk, n - start);
			if (count > 0)
				Array.Fill(fb, linearPremultiplied, start, count);
		});
	}

	/// <summary>Rasterize one frame's draw list onto the framebuffer (over whatever <see cref="Clear"/> left).</summary>
	/// <param name="paperWhite">HDR paper-white linear scale (ui.slang's <c>pc.paperWhite</c>); applied only when the
	/// list targets an HDR surface. 1.0 leaves SDR untouched.</param>
	public void Render(DrawListData draw, float paperWhite = 1f)
	{
		TrianglesDrawn = 0;
		SkippedCommands = 0;
		LayersComposited = 0;
		_layers.Clear();

		var verts = draw.Vertices.Span;
		var indices = draw.Indices.Span;
		var commands = draw.Commands.Span;
		var hdr = draw.IsHdrTarget;
		ScanCommands(commands);
		BindSlots(draw.TextureSlots);

		for (var ci = 0; ci < commands.Length; ci++)
		{
			ref readonly var cmd = ref commands[ci];
			var kind = (UIPrimKind)cmd.Tag;

			// Group-opacity layers (zero-geometry markers). BeginLayer redirects subsequent draws into a fresh
			// transparent framebuffer; EndLayer composites it back at the group's fade alpha (read off the BeginLayer
			// marker's degenerate quad). The fade is applied ONLY here — primitive shading already lifted HDR
			// paper-white per pixel, which commutes with the linear premultiplied alpha-over, so the composite must
			// not lift again.
			if (kind == UIPrimKind.BeginLayer)
			{
				var alpha = (float)verts[cmd.VertexOffset].Color.A;
				var rect = Intersect(PixelBounds(cmd.ClipRect), _layers.Count > 0 ? _layers.Peek().Rect : FullSurface);
				var fb = RentLayer(_layers.Count);
				for (var y = rect.Y0; y <= rect.Y1; y++)
					Array.Clear(fb, y * _width + rect.X0, rect.X1 - rect.X0 + 1);
				_layers.Push((fb, alpha, rect));
				continue;
			}
			if (kind == UIPrimKind.EndLayer)
			{
				if (_layers.Count == 0)
					continue;
				var (layerFb, layerAlpha, rect) = _layers.Pop();
				var dst = ActiveTarget;
				for (var y = rect.Y0; y <= rect.Y1; y++)
				{
					for (var i = y * _width + rect.X0; i <= y * _width + rect.X1; i++)
					{
						var src = layerFb[i] * layerAlpha; // fade the premultiplied layer by the group alpha
						dst[i] = src + dst[i] * (1f - src.W);
					}
				}
				LayersComposited++;
				continue;
			}

			// Texture-sampling kinds need their slot; skip (and count) a command whose slot the list doesn't have.
			Texture? tex = null;
			if (kind is UIPrimKind.Glyph or UIPrimKind.Image or UIPrimKind.Vector && !TryGetSlot(cmd.TextureIndex, out tex))
			{
				SkippedCommands++;
				continue;
			}

			var target = ActiveTarget;

			// Scissor → inclusive integer pixel bounds, clamped to the framebuffer and to the open layer's rect.
			var scissor = Intersect(PixelBounds(cmd.ClipRect), _layers.Count > 0 ? _layers.Peek().Rect : FullSurface);
			var (sx0, sy0, sx1, sy1) = (scissor.X0, scissor.Y0, scissor.X1, scissor.Y1);
			if (scissor.IsEmpty)
				continue;

			// Indices are 16-bit and relative to the command's VertexOffset, the base vertex every backend draws with.
			var end = cmd.IndexOffset + cmd.IndexCount;
			for (var i = cmd.IndexOffset; i + 3 <= end; i += 3)
			{
				var v0 = verts[cmd.VertexOffset + indices[i]];
				var v1 = verts[cmd.VertexOffset + indices[i + 1]];
				var v2 = verts[cmd.VertexOffset + indices[i + 2]];
				RasterizeTriangle(target, v0, v1, v2, kind, tex, sx0, sy0, sx1, sy1, hdr, paperWhite);
				TrianglesDrawn++;
			}
		}
	}

	/// <summary>
	/// Multithreaded twin of <see cref="Render"/> for the no-group-opacity case. The framebuffer is split into
	/// horizontal bands and each band replays the whole command list (clamped to its own rows) on its own thread.
	/// When <paramref name="clearColor"/> is supplied, each band first fills its own rows with it — fusing the clear
	/// into the same pass, so the just-cleared rows are still hot in cache when the raster blends over them (no
	/// separate full-framebuffer clear pass). Pass <c>null</c> to render over existing contents (e.g. a second draw
	/// list onto an already-cleared frame). Bands write disjoint rows and every pixel is shaded by exactly the command
	/// the serial path would use, in the same order, so the output is byte-identical to a serial
	/// <see cref="Clear"/>-then-<see cref="Render"/>. Falls back to that serial pair when the list carries
	/// group-opacity layers, whose offscreen compositing spans the whole surface and is not band-local.
	/// </summary>
	public void RenderParallel(DrawListData draw, Vector4? clearColor, float paperWhite = 1f) =>
		RenderParallel(draw, clearColor, 0, 0, _width - 1, _height - 1, paperWhite);

	/// <summary>
	/// As <see cref="RenderParallel(DrawListData, Vector4?, float)"/>, but limited to the inclusive pixel rectangle
	/// [<paramref name="rx0"/>..<paramref name="rx1"/>] × [<paramref name="ry0"/>..<paramref name="ry1"/>] — the
	/// dirty-rectangle fast path. Bands cover only the region's rows, the fused clear fills only the region, and every
	/// command is replayed scissored to the region (so an overlapped pixel is still composed from all its commands in
	/// order). Pixels outside the region are left untouched.
	/// </summary>
	public void RenderParallel(DrawListData draw, Vector4? clearColor, int rx0, int ry0, int rx1, int ry1, float paperWhite = 1f)
	{
		SkippedCommands = 0;
		LayersComposited = 0;

		rx0 = Math.Max(0, rx0);
		ry0 = Math.Max(0, ry0);
		rx1 = Math.Min(_width - 1, rx1);
		ry1 = Math.Min(_height - 1, ry1);
		if (rx0 > rx1 || ry0 > ry1)
			return;

		if (ScanCommands(draw.Commands.Span))
		{
			// Layers composite through whole-surface offscreen targets, so a region-limited replay isn't valid; do the
			// full serial pair. (The compositor reports whole-surface damage when layers are present, so the region is
			// the full frame here anyway.)
			if (clearColor is { } layerClear)
				Clear(layerClear);
			Render(draw, paperWhite);
			return;
		}

		BindSlots(draw.TextureSlots);
		var hdr = draw.IsHdrTarget;
		var verts = draw.Vertices;
		var indices = draw.Indices;
		var commands = draw.Commands;
		var fb = _fb;
		var width = _width;
		var fullWidth = rx0 == 0 && rx1 == width - 1;

		const int bandHeight = 16;
		var bands = (ry1 - ry0 + bandHeight) / bandHeight;

		var triangles = 0;
		Parallel.For(0, bands, () => 0, (b, _, local) =>
		{
			var y0 = ry0 + b * bandHeight;
			var y1 = Math.Min(ry1, y0 + bandHeight - 1);
			if (clearColor is { } cc) // fused clear: these rows stay hot for the raster
			{
				if (fullWidth)
					Array.Fill(fb, cc, y0 * width, (y1 - y0 + 1) * width);
				else
					for (var y = y0; y <= y1; y++)
						Array.Fill(fb, cc, y * width + rx0, rx1 - rx0 + 1);
			}
			return local + RasterizeBand(fb, verts.Span, indices.Span, commands.Span, y0, y1, rx0, rx1, hdr, paperWhite);
		}, local => Interlocked.Add(ref triangles, local));
		TrianglesDrawn = triangles;
	}

	// Rasterize every command's triangles clipped to rows [bandY0, bandY1] — the body of Render's command loop with
	// the scissor's vertical extent intersected against the band. Returns the triangles touched (for the perf counter).
	private int RasterizeBand(Vector4[] target, ReadOnlySpan<OverlayVertex> verts, ReadOnlySpan<ushort> indices,
		ReadOnlySpan<DrawCommand> commands, int bandY0, int bandY1, int bandX0, int bandX1, bool hdr, float paperWhite)
	{
		var triangles = 0;
		for (var ci = 0; ci < commands.Length; ci++)
		{
			ref readonly var cmd = ref commands[ci];
			var kind = (UIPrimKind)cmd.Tag;

			Texture? tex = null;
			if (kind is UIPrimKind.Glyph or UIPrimKind.Image or UIPrimKind.Vector && !TryGetSlot(cmd.TextureIndex, out tex))
				continue;

			var clip = cmd.ClipRect;
			var sx0 = Math.Max(bandX0, (int)MathF.Floor(clip.X));
			var sy0 = Math.Max(bandY0, (int)MathF.Floor(clip.Y));
			var sx1 = Math.Min(bandX1, (int)MathF.Ceiling(clip.Right) - 1);
			var sy1 = Math.Min(bandY1, (int)MathF.Ceiling(clip.Bottom) - 1);
			if (sx0 > sx1 || sy0 > sy1)
				continue;

			var end = cmd.IndexOffset + cmd.IndexCount;
			for (var i = cmd.IndexOffset; i + 3 <= end; i += 3)
			{
				var v0 = verts[cmd.VertexOffset + indices[i]];
				var v1 = verts[cmd.VertexOffset + indices[i + 1]];
				var v2 = verts[cmd.VertexOffset + indices[i + 2]];
				RasterizeTriangle(target, v0, v1, v2, kind, tex, sx0, sy0, sx1, sy1, hdr, paperWhite);
				triangles++;
			}
		}
		return triangles;
	}

	// Check every command's tag before anything is drawn, and report whether the list opens group-opacity layers. A tag
	// that names no kind this rasterizer draws throws: shading it as some other kind would hide a mismatch between the
	// producer and the oracle. The tag is matched as the whole uint, because casting it to the byte-sized UIPrimKind first
	// would alias 256 to Fill.
	private static bool ScanCommands(ReadOnlySpan<DrawCommand> commands)
	{
		var layers = false;
		for (var i = 0; i < commands.Length; i++)
		{
			switch (commands[i].Tag)
			{
				case (uint)UIPrimKind.Fill or (uint)UIPrimKind.RoundedRect or (uint)UIPrimKind.Border
					or (uint)UIPrimKind.Glyph or (uint)UIPrimKind.Image or (uint)UIPrimKind.Vector:
					break;
				case (uint)UIPrimKind.BeginLayer or (uint)UIPrimKind.EndLayer:
					layers = true;
					break;
				default:
					throw new ArgumentException(
						$"Draw command {i} has tag {commands[i].Tag}, which is not a primitive kind the rasterizer draws.", "draw");
			}
		}
		return layers;
	}

	/// <summary>
	/// Resolve the framebuffer straight into a caller-provided BGRA8 buffer — the byte order a GDI 32bpp DIB wants —
	/// in parallel across rows. Same per-pixel un-premultiply + sRGB encode as <see cref="ResolveToRgba8"/>, fused
	/// with the channel swap and written into <paramref name="dest"/>, so a window host needs no extra copy or
	/// per-frame allocation. <paramref name="dest"/> must hold at least width*height*4 bytes.
	/// </summary>
	public void ResolveToBgra8(byte[] dest) => ResolveToBgra8(dest, 0, 0, _width - 1, _height - 1);

	/// <summary>
	/// As <see cref="ResolveToBgra8(byte[])"/>, but resolves only the inclusive pixel rectangle
	/// [<paramref name="rx0"/>..<paramref name="rx1"/>] × [<paramref name="ry0"/>..<paramref name="ry1"/>] — the
	/// dirty-rectangle fast path. Those pixels are written at their full-frame offsets in <paramref name="dest"/>;
	/// the rest of <paramref name="dest"/> is left as the previous frame's bytes.
	/// </summary>
	public void ResolveToBgra8(byte[] dest, int rx0, int ry0, int rx1, int ry1)
	{
		if (dest.Length < _width * _height * 4)
			throw new ArgumentException("destination buffer too small for the framebuffer", nameof(dest));

		rx0 = Math.Max(0, rx0);
		ry0 = Math.Max(0, ry0);
		rx1 = Math.Min(_width - 1, rx1);
		ry1 = Math.Min(_height - 1, ry1);
		if (rx0 > rx1 || ry0 > ry1)
			return;

		var fb = _fb;
		var width = _width;
		Parallel.For(ry0, ry1 + 1, y =>
		{
			var row = y * width;
			for (var x = rx0; x <= rx1; x++)
			{
				var c = fb[row + x];
				var a = Saturate(c.W);
				var inv = a > 1e-6f ? 1f / a : 0f; // un-premultiply
				var o = (row + x) * 4;
				dest[o + 0] = SrgbByte(c.Z * inv); // B
				dest[o + 1] = SrgbByte(c.Y * inv); // G
				dest[o + 2] = SrgbByte(c.X * inv); // R
				dest[o + 3] = (byte)MathF.Round(a * 255f);
			}
		});
	}

	/// <summary>
	/// Resolve the linear-premultiplied framebuffer to straight (un-premultiplied) sRGB RGBA8 — the bytes a golden
	/// PNG/BMP holds. Mirrors the GPU store: blend stayed linear-premultiplied, the sRGB target re-encoded on write.
	/// </summary>
	public byte[] ResolveToRgba8()
	{
		var px = new byte[_width * _height * 4];
		for (var i = 0; i < _fb.Length; i++)
		{
			var c = _fb[i];
			var a = Saturate(c.W);
			var inv = a > 1e-6f ? 1f / a : 0f; // un-premultiply
			var o = i * 4;
			px[o + 0] = ToSrgbByte(c.X * inv);
			px[o + 1] = ToSrgbByte(c.Y * inv);
			px[o + 2] = ToSrgbByte(c.Z * inv);
			px[o + 3] = (byte)MathF.Round(a * 255f);
		}
		return px;
	}

	private void RasterizeTriangle(
		Vector4[] target,
		in OverlayVertex va, in OverlayVertex vb, in OverlayVertex vc,
		UIPrimKind kind, Texture? tex, int sx0, int sy0, int sx1, int sy1, bool hdr, float paperWhite)
	{
		Vector2 p0 = va.Position, p1 = vb.Position, p2 = vc.Position;

		var area = Edge(p0, p1, p2);
		if (MathF.Abs(area) < 1e-7f)
			return; // degenerate
		var inv = 1f / area;

		// Triangle bbox, clamped to the scissor box.
		var minX = Math.Max(sx0, (int)MathF.Floor(Min3(p0.X, p1.X, p2.X)));
		var maxX = Math.Min(sx1, (int)MathF.Ceiling(Max3(p0.X, p1.X, p2.X)) - 1);
		var minY = Math.Max(sy0, (int)MathF.Floor(Min3(p0.Y, p1.Y, p2.Y)));
		var maxY = Math.Min(sy1, (int)MathF.Ceiling(Max3(p0.Y, p1.Y, p2.Y)) - 1);
		if (minX > maxX || minY > maxY)
			return;

		// Screen-space gradients of the barycentric weights are constant across an affinely-interpolated triangle,
		// so the uv step per +1 screen pixel (duv/dx, duv/dy) is too. We use it to evaluate the SDF one pixel over
		// for a deterministic, analytic stand-in for the GPU's quad-based fwidth() — see Shade().
		var dw0dx = -(p2.Y - p1.Y) * inv; var dw0dy = (p2.X - p1.X) * inv;
		var dw1dx = -(p0.Y - p2.Y) * inv; var dw1dy = (p0.X - p2.X) * inv;
		var dw2dx = -(p1.Y - p0.Y) * inv; var dw2dy = (p1.X - p0.X) * inv;
		var duvdx = dw0dx * va.Uv + dw1dx * vb.Uv + dw2dx * vc.Uv;
		var duvdy = dw0dy * va.Uv + dw1dy * vb.Uv + dw2dy * vc.Uv;

		// The Vector prim's distances are linear across the triangle, so their screen-space gradients are constant: the
		// exact values the GPU's ddx/ddy give. Each distance over its gradient's length is its edge's pixel distance.
		var dpdx = dw0dx * va.Custom + dw1dx * vb.Custom + dw2dx * vc.Custom;
		var dpdy = dw0dy * va.Custom + dw1dy * vb.Custom + dw2dy * vc.Custom;
		var gradient = Vector4.SquareRoot(dpdx * dpdx + dpdy * dpdy);

		Vector4 c0 = va.Color.ToVector4(), c1 = vb.Color.ToVector4(), c2 = vc.Color.ToVector4();

		for (var y = minY; y <= maxY; y++)
		{
			for (var x = minX; x <= maxX; x++)
			{
				var pt = new Vector2(x + 0.5f, y + 0.5f); // pixel centre
				var w0 = Edge(p1, p2, pt) * inv;
				var w1 = Edge(p2, p0, pt) * inv;
				var w2 = Edge(p0, p1, pt) * inv;
				if (w0 < -NearEdge || w1 < -NearEdge || w2 < -NearEdge)
					continue; // outside (winding handled by the sign of inv)
							  // A centre on or within rounding of an edge: decide it as a GPU does, so a pixel on an edge two triangles
							  // share (a quad's diagonal, the cut between two gradient bands) is drawn by exactly one of them.
				if ((w0 <= NearEdge || w1 <= NearEdge || w2 <= NearEdge) && !Owns(p0, p1, p2, pt, area > 0f))
					continue;

				var uv = w0 * va.Uv + w1 * vb.Uv + w2 * vc.Uv;
				var col = w0 * c0 + w1 * c1 + w2 * c2;     // premultiplied, linear
				var prm = w0 * va.Custom + w1 * vb.Custom + w2 * vc.Custom;

				var src = Shade(kind, uv, col, prm, duvdx, duvdy, gradient, tex);
				if (hdr)
				{
					src.X *= paperWhite;
					src.Y *= paperWhite;
					src.Z *= paperWhite;
				}

				// Premultiplied alpha-over (src=ONE, dst=ONE_MINUS_SRC_ALPHA), in linear space.
				var idx = y * _width + x;
				target[idx] = src + target[idx] * (1f - src.W);
			}
		}
	}

	// ── ui.slang fragmentMain, per kind ───────────────────────────────────────────────────────────────────────
	// colour arrives premultiplied; each kind multiplies it by a coverage in [0,1] (which keeps it premultiplied).

	private static Vector4 Shade(UIPrimKind kind, Vector2 uv, Vector4 col, Vector4 prm, Vector2 duvdx, Vector2 duvdy, Vector4 gradient, Texture? tex)
	{
		switch (kind)
		{
			case UIPrimKind.Vector:
				{
					// Four half-plane distances (params), each turned into a one-pixel coverage ramp centred on its edge;
					// an unused slot is a constant, whose zero gradient makes its factor 1. Then the slot's texel.
					var coverage = EdgeCoverage(prm.X, gradient.X) * EdgeCoverage(prm.Y, gradient.Y)
						* EdgeCoverage(prm.Z, gradient.Z) * EdgeCoverage(prm.W, gradient.W);
					return col * coverage * tex!.Sample(uv);
				}
			case UIPrimKind.Glyph:
				{
					// MTSDF text: median of the RGB distances, recentred at 0.5, then screenPxRange-scaled coverage.
					// params.x (Custom.X) carries the per-run screenPxRange. The .a channel is a true SDF, unused here.
					var s = tex!.Sample(uv);
					var sd = Median3(s.X, s.Y, s.Z) - 0.5f;
					return col * Saturate(sd * prm.X + 0.5f);
				}
			case UIPrimKind.Image:
				// Tinted blit: sample the source and multiply by the (premultiplied) vertex colour.
				return col * tex!.Sample(uv);
			case UIPrimKind.RoundedRect:
				{
					// params = (cornerRadius, _, sizeX, sizeY); uv is 0..1 across the rect, scaled to local pixels.
					var size = new Vector2(prm.Z, prm.W);
					var p = uv * size - size * 0.5f;
					var dpdx = duvdx * size;
					var dpdy = duvdy * size;
					return col * Coverage(p, dpdx, dpdy, size * 0.5f, prm.X);
				}
			case UIPrimKind.Border:
				{
					// Outer rounded box minus the inner one inset by the (uniform) thickness → the ring.
					var size = new Vector2(prm.Z, prm.W);
					var p = uv * size - size * 0.5f;
					var dpdx = duvdx * size;
					var dpdy = duvdy * size;
					var half = size * 0.5f;
					var radius = prm.X;
					var thick = prm.Y;
					var outer = Coverage(p, dpdx, dpdy, half, radius);
					var inner = Coverage(p, dpdx, dpdy, half - new Vector2(thick), MathF.Max(radius - thick, 0f));
					return col * Saturate(outer - inner);
				}
			case UIPrimKind.Fill: // solid premultiplied quad, no SDF, no texture
				return col;
			default:
				// Render validates every tag first, so this is unreachable; it stays loud rather than guessing a fill.
				throw new InvalidOperationException($"The rasterizer has no shading for primitive kind {kind}.");
		}
	}

	// Coverage of a rounded box at p with one-pixel analytic AA. Evaluates the SDF at p and one screen pixel over in x and
	// y for its screen-space derivatives (the GPU's ddx/ddy), and divides the distance by the gradient's length: the
	// pixel distance to the edge, the same at any angle, which fwidth's |ddx| + |ddy| over-blurs by up to √2 at 45°.
	private static float Coverage(Vector2 p, Vector2 dpdx, Vector2 dpdy, Vector2 half, float radius)
	{
		var d = SdRoundedBox(p, half, radius);
		var ddx = SdRoundedBox(p + dpdx, half, radius) - d;
		var ddy = SdRoundedBox(p + dpdy, half, radius) - d;
		return EdgeCoverage(-d, MathF.Sqrt(ddx * ddx + ddy * ddy));
	}

	// The coverage of a pixel whose centre is `distance` inside an edge (negative outside), the distance in units whose
	// screen-space gradient has length `gradient`: a one-pixel linear ramp centred on the edge.
	private static float EdgeCoverage(float distance, float gradient) => Saturate(0.5f + distance / MathF.Max(gradient, 1e-6f));

	// Signed distance to a rounded box (iquilezles.org/articles/distfunctions2d). Negative inside.
	private static float SdRoundedBox(Vector2 p, Vector2 b, float r)
	{
		var q = Vector2.Abs(p) - b + new Vector2(r);
		var outside = new Vector2(MathF.Max(q.X, 0f), MathF.Max(q.Y, 0f)).Length();
		return MathF.Min(MathF.Max(q.X, q.Y), 0f) + outside - r;
	}

	private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
		(b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

	// How close (as a barycentric weight) a pixel centre must be to an edge before the fill rule decides it rather than
	// the plain inside test: wide enough to cover the rounding between two triangles' views of a shared edge.
	private const float NearEdge = 1e-5f;

	// The GPU fill rule for a pixel centre at or near an edge: inside each edge, decided from that edge's own canonical
	// orientation so two triangles sharing it see exactly opposite values, and a centre exactly on an edge belongs to the
	// triangle for which that edge is a top or left one (Direct3D's and Vulkan's top-left rule).
	private static bool Owns(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 pt, bool positive) =>
		InsideEdge(p1, p2, pt, positive) && InsideEdge(p2, p0, pt, positive) && InsideEdge(p0, p1, pt, positive);

	private static bool InsideEdge(Vector2 a, Vector2 b, Vector2 p, bool positive)
	{
		if (!positive)
			(a, b) = (b, a); // orient the edge so the triangle lies on its positive side
		var e = Precedes(a, b) ? Edge(a, b, p) : -Edge(b, a, p);
		if (e != 0f)
			return e > 0f;
		// On the edge. In y-down pixels, with the triangle on the positive side, a top edge runs horizontally rightwards
		// (the triangle below it) and a left edge runs upwards (the triangle to its right).
		var d = b - a;
		return d.Y < 0f || (d.Y == 0f && d.X > 0f);
	}

	private static bool Precedes(Vector2 a, Vector2 b) => a.X < b.X || (a.X == b.X && a.Y < b.Y);

	private static float Median3(float a, float b, float c) =>
		MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));

	private static float Saturate(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

	private static byte ToSrgbByte(float linear)
	{
		var c = Saturate(linear);
		var s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
		return (byte)MathF.Round(Saturate(s) * 255f);
	}

	// 16-bit linear → sRGB byte table: the per-pixel sRGB encode (a MathF.Pow) replaced by a 64 KB lookup. Built once
	// from the exact ToSrgbByte, so a lookup at round(linear * 65535) reproduces it. 16 bits keeps the quantization
	// error below the steepest (near-black) byte step, so it stays byte-identical — the gallery's --selftest asserts
	// this against the serial ResolveToRgba8, which keeps using the exact encode.
	private static readonly byte[] SrgbLut = BuildSrgbLut();

	private static byte[] BuildSrgbLut()
	{
		var lut = new byte[65536];
		for (var i = 0; i < lut.Length; i++)
			lut[i] = ToSrgbByte(i / 65535f);
		return lut;
	}

	private static byte SrgbByte(float linear) => SrgbLut[(int)(Saturate(linear) * 65535f + 0.5f)];

	private static float Min3(float a, float b, float c) => MathF.Min(a, MathF.Min(b, c));
	private static float Max3(float a, float b, float c) => MathF.Max(a, MathF.Max(b, c));
}
