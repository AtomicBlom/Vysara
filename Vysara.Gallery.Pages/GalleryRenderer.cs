using System.Diagnostics;
using System.Numerics;
using System.Text;

using Vysara.Drawing;
using Vysara.HarfBuzz;
using Vysara.Input;
using Vysara.Rasterizer;
using Vysara.Text;

namespace Vysara.Gallery;

/// <summary>
/// Drives the gallery's Vysara control tree through the CPU <see cref="SoftwareRasterizer"/> — window- and
/// backend-agnostic. The frame splits into a cheap <see cref="Compose"/> (advance + lay out + composite, which the
/// compositor no-ops when nothing changed) and an expensive <see cref="PresentTo"/> (multithreaded raster + fused
/// BGRA resolve). A host calls <see cref="Compose"/> every frame and only calls <see cref="PresentTo"/> when the
/// returned damage key changed, so a static frame costs almost nothing. Owns the <see cref="UIHost"/>, the bound
/// font page, and a size-matched rasterizer (rebuilt when the surface size changes).
/// </summary>
internal sealed class GalleryRenderer : IDisposable
{
	private readonly UIHost _host = new();
	private readonly UIView _view;
	private readonly IGalleryView _content;
	// Baked once for the process: every renderer (the window's, a self-test's pair, each golden test's) shares it.
	private static readonly Lazy<FontAtlas> SharedFont = new(BuildShapedFont);
	private readonly Vector4 _clear = GalleryTheme.PageBackground.ToPremultiplied().ToVector4();
	private SoftwareRasterizer? _rasterizer;
	private double _lastSeconds;
	private double _tickMs, _composeMs;

	/// <summary>A renderer drawing <paramref name="content"/>: the shell, or one page on its own.</summary>
	public GalleryRenderer(IGalleryView content)
	{
		_content = content;
		// Lay text out and draw it with the glyph-id atlas, so its HarfBuzz shaper drives every glyph run (ligatures
		// included) and measuring and drawing agree.
		_view = _host.CreateRoot(UILayer.Default, interactive: true);
		_view.Root.Font = SharedFont.Value;
		_view.Root.Content = content.Root;
	}

	// Bake a glyph-id-keyed Cascadia atlas (printable ASCII + the GSUB-reachable ligature/contextual glyphs) from the
	// embedded font program, and install a HarfBuzz shaper on it — so the gallery's text is really shaped, and
	// programming ligatures (-> => != == >= etc.) resolve to their contextual glyphs.
	private static FontAtlas BuildShapedFont()
	{
		var fontBytes = ReadEmbeddedFont();
		var bake = MsdfBaker.BakeFont(fontBytes, "gallery",
			new BakeOptions { Charset = ["ascii"], GlyphIdKeyed = true, AtlasPageSize = 4096 });
		using var json = new MemoryStream(Encoding.UTF8.GetBytes(bake.FontJson));
		using var png = new MemoryStream(bake.PngPage);
		var atlas = FontAtlas.Load(json, png);
		atlas.Shaper = new HarfBuzzTextShaper(fontBytes);
		return atlas;
	}

	private static byte[] ReadEmbeddedFont()
	{
		using var stream = typeof(GalleryRenderer).Assembly.GetManifestResourceStream("CascadiaCode.ttf")
			?? throw new InvalidOperationException("Embedded font 'CascadiaCode.ttf' not found.");
		using var ms = new MemoryStream();
		stream.CopyTo(ms);
		return ms.ToArray();
	}

	/// <summary>The DIP→pixel scale (display DPI / 96). Layout runs in DIPs and the render walk scales to physical
	/// pixels, so the UI stays crisp at any display scale; the host sets this from the window's monitor DPI.</summary>
	public float Scale
	{
		get => _view.Root.LayoutScale;
		set => _view.Root.LayoutScale = value;
	}

	/// <summary>How tall the content wants to be, in physical pixels, laid out <paramref name="pixelWidth"/> wide: the height
	/// a capture of a whole page takes.</summary>
	public int DesiredHeight(int pixelWidth)
	{
		_view.Root.LayoutPass(new Size(pixelWidth, 100_000f));
		return Math.Max(1, (int)MathF.Ceiling(_content.Root.DesiredSize.Height * Scale));
	}

	/// <summary>Take the content out of the live tree, so the brushes the gallery's static styles share stop listening to it
	/// and a capture's tree can be collected once it is drawn.</summary>
	public void Dispose() => _view.Root.Content = null;

	/// <summary>Per-phase wall-clock cost of the last presented frame (for the <c>--bench</c> diagnostic).</summary>
	public FrameTimings LastTimings { get; private set; }

	public void QueuePointer(in PointerInput e) => _host.QueuePointer(e);
	public void QueueKey(in KeyInput e) => _host.QueueKey(e);
	public void QueueText(ReadOnlySpan<char> text) => _host.QueueText(text);

	/// <summary>
	/// Advance animations, lay out, and composite the control tree against a <paramref name="width"/> ×
	/// <paramref name="height"/> surface — the cheap half of a frame. Returns a key that changes only when the next
	/// rasterized frame would differ (content revision + size), so a host can skip <see cref="PresentTo"/> otherwise.
	/// </summary>
	public long Compose(int width, int height, double seconds)
	{
		EnsureSize(width, height);
		var dt = seconds - _lastSeconds;
		_lastSeconds = seconds;

		var t0 = Stopwatch.GetTimestamp();
		_content.Animate(seconds);
		_host.Tick(dt, (float)seconds, new Vector2(width, height));
		var t1 = Stopwatch.GetTimestamp();
		_host.Render();
		var t2 = Stopwatch.GetTimestamp();
		_tickMs = Ms(t0, t1);
		_composeMs = Ms(t1, t2);
		_rasterizer!.ReleaseTextures(_host.ReleasedTextures);

		// A texel change (a rewritten bitmap) moves only the texture revision, so both revisions key the frame.
		long key = (width * 73856093L) ^ (height * 19349663L);
		foreach (var (_, data) in _host.DrawLists)
			key = (key * 31 + data.Revision) * 31 + data.TextureRevision;
		return key;
	}

	/// <summary>
	/// Rasterize the last <see cref="Compose"/> and resolve it into <paramref name="bgra"/> (the GDI DIB byte order),
	/// using the multithreaded raster + resolve. Only the changed region is repainted — the compositor's per-frame
	/// damage rectangle — unless <paramref name="forceFull"/> (a resized buffer or an OS repaint). Returns the region
	/// it touched, so the host knows what changed. <paramref name="bgra"/> must be width*height*4 bytes and is left
	/// holding the previous frame's bytes outside the region.
	/// </summary>
	public PixelRect PresentTo(byte[] bgra, bool forceFull)
	{
		var r = _rasterizer!;
		var region = forceFull ? PixelRect.Full(r.Width, r.Height) : DamageRegion(r.Width, r.Height);

		var t0 = Stopwatch.GetTimestamp();
		var cleared = false; // fuse the clear into the first list's raster bands; later lists render over it
		foreach (var (_, data) in _host.DrawLists)
		{
			r.RenderParallel(data, cleared ? null : _clear, region.X0, region.Y0, region.X1, region.Y1);
			cleared = true;
		}
		var t1 = Stopwatch.GetTimestamp();
		r.ResolveToBgra8(bgra, region.X0, region.Y0, region.X1, region.Y1);
		var t2 = Stopwatch.GetTimestamp();

		LastTimings = new FrameTimings(_tickMs, _composeMs, Ms(t0, t1), Ms(t1, t2));
		return region;
	}

	// The union of this frame's per-surface damage rectangles, in physical pixels, expanded by a slop margin for
	// analytic-AA bleed and clamped to the framebuffer (inclusive bounds). Falls back to the whole frame if no surface
	// reported damage (shouldn't happen once a present is triggered, but repaint-all is the safe default).
	private PixelRect DamageRegion(int width, int height)
	{
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		var any = false;
		foreach (var (_, data) in _host.DrawLists)
		{
			var d = data.Damage;
			if (d.IsEmpty)
				continue;
			minX = MathF.Min(minX, d.X); minY = MathF.Min(minY, d.Y);
			maxX = MathF.Max(maxX, d.Right); maxY = MathF.Max(maxY, d.Bottom);
			any = true;
		}
		if (!any)
			return PixelRect.Full(width, height);

		const int margin = 2; // analytic AA can touch ~1px past a quad edge; a little slop keeps it exact
		var x0 = Math.Max(0, (int)MathF.Floor(minX) - margin);
		var y0 = Math.Max(0, (int)MathF.Floor(minY) - margin);
		var x1 = Math.Min(width - 1, (int)MathF.Ceiling(maxX) - 1 + margin);
		var y1 = Math.Min(height - 1, (int)MathF.Ceiling(maxY) - 1 + margin);
		return new PixelRect(x0, y0, x1, y1);
	}

	/// <summary>Compose a frame at <paramref name="seconds"/> and return its serial reference render (see
	/// <see cref="RasterizeSerialRgba"/>), so the headless capture's output exactly matches the golden
	/// software-rasterizer path.</summary>
	public byte[] Render(int width, int height, double seconds)
	{
		Compose(width, height, seconds);
		return RasterizeSerialRgba();
	}

	/// <summary>Serial raster of the last <see cref="Compose"/>, resolved to straight RGBA8 through the exact sRGB
	/// encode — the reference the parallel raster is byte-identical to.</summary>
	public byte[] RasterizeSerialRgba()
	{
		var r = _rasterizer!;
		r.Clear(_clear);
		foreach (var (_, data) in _host.DrawLists)
			r.Render(data);
		return r.ResolveToRgba8();
	}

	/// <summary>For the self-test: the parallel raster of the last <see cref="Compose"/>, resolved through the
	/// <em>exact</em> (non-LUT) encode, so a diff against <see cref="RasterizeSerialRgba"/> of the same frame isolates
	/// raster correctness from the present path's resolve-LUT approximation.</summary>
	public byte[] RasterizeParallelRgba()
	{
		var r = _rasterizer!;
		var cleared = false;
		foreach (var (_, data) in _host.DrawLists)
		{
			r.RenderParallel(data, cleared ? null : _clear);
			cleared = true;
		}
		return r.ResolveToRgba8();
	}

	private void EnsureSize(int width, int height)
	{
		if (_rasterizer is { } r && r.Width == width && r.Height == height)
			return;
		_rasterizer = new SoftwareRasterizer(width, height);
	}

	private static double Ms(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
}

/// <summary>Wall-clock cost of one frame's phases, in milliseconds.</summary>
internal readonly record struct FrameTimings(double TickMs, double ComposeMs, double RasterMs, double ResolveMs)
{
	public double TotalMs => TickMs + ComposeMs + RasterMs + ResolveMs;
}

/// <summary>An inclusive integer pixel rectangle (the dirty region a present touched).</summary>
internal readonly record struct PixelRect(int X0, int Y0, int X1, int Y1)
{
	public static PixelRect Full(int width, int height) => new(0, 0, width - 1, height - 1);
	public int Width => X1 - X0 + 1;
	public int Height => Y1 - Y0 + 1;
}
