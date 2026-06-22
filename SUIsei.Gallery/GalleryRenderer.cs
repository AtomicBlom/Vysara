using System.Diagnostics;
using System.Numerics;
using SUIsei.Drawing;
using SUIsei.Input;
using SUIsei.Rasterizer;

namespace SUIsei.Gallery;

/// <summary>
/// Drives the gallery's SUIsei control tree through the CPU <see cref="SoftwareRasterizer"/> — window- and
/// backend-agnostic. The frame splits into a cheap <see cref="Compose"/> (advance + lay out + composite, which the
/// compositor no-ops when nothing changed) and an expensive <see cref="PresentTo"/> (multithreaded raster + fused
/// BGRA resolve). A host calls <see cref="Compose"/> every frame and only calls <see cref="PresentTo"/> when the
/// returned damage key changed, so a static frame costs almost nothing. Owns the <see cref="UIHost"/>, the bound
/// font page, and a size-matched rasterizer (rebuilt when the surface size changes).
/// </summary>
internal sealed class GalleryRenderer
{
	private readonly UIHost _host = new();
	private readonly UIView _view;
	private readonly GalleryView _gallery = new();
	private readonly Texture _fontPage = Texture.FromAtlas(FontAtlas.Default.Image);
	private readonly Vector4 _clear = GalleryView.PageBackground.ToPremultiplied().ToVector4();
	private readonly HashSet<int> _boundImages = [];
	private SoftwareRasterizer? _rasterizer;
	private double _lastSeconds;
	private double _tickMs, _composeMs;

	public GalleryRenderer(Action onQuit)
	{
		_gallery.Quit = onQuit;
		_view = _host.CreateRoot(UIPlacement.Overlay(0, 1f), interactive: true);
		_view.Root.Content = _gallery.Root;
	}

	/// <summary>The DIP→pixel scale (display DPI / 96). Layout runs in DIPs and the render walk scales to physical
	/// pixels, so the UI stays crisp at any display scale; the host sets this from the window's monitor DPI.</summary>
	public float Scale
	{
		get => _view.Root.LayoutScale;
		set => _view.Root.LayoutScale = value;
	}

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
		_host.Tick(dt, (float)seconds, new Vector2(width, height));
		var t1 = Stopwatch.GetTimestamp();
		_host.Render();
		var t2 = Stopwatch.GetTimestamp();
		_tickMs = Ms(t0, t1);
		_composeMs = Ms(t1, t2);

		long key = (width * 73856093L) ^ (height * 19349663L);
		foreach (var (_, data) in _host.DrawLists)
			key = key * 31 + data.Revision;
		return key;
	}

	/// <summary>Rasterize the last <see cref="Compose"/> and resolve it into <paramref name="bgra"/> (the GDI DIB
	/// byte order) using the multithreaded raster + resolve. <paramref name="bgra"/> must be width*height*4 bytes.</summary>
	public void PresentTo(byte[] bgra)
	{
		var r = _rasterizer!;
		BindImages(r);
		var t0 = Stopwatch.GetTimestamp();
		r.Clear(_clear);
		foreach (var (_, data) in _host.DrawLists)
			r.RenderParallel(data);
		var t1 = Stopwatch.GetTimestamp();
		r.ResolveToBgra8(bgra);
		var t2 = Stopwatch.GetTimestamp();

		LastTimings = new FrameTimings(_tickMs, _composeMs, Ms(t0, t1), Ms(t1, t2));
	}

	/// <summary>Serial reference render returning straight RGBA8 — used by the headless capture so its output exactly
	/// matches the golden software-rasterizer path (the parallel raster is byte-identical to this).</summary>
	public byte[] Render(int width, int height, double seconds)
	{
		Compose(width, height, seconds);
		var r = _rasterizer!;
		BindImages(r);
		r.Clear(_clear);
		foreach (var (_, data) in _host.DrawLists)
			r.Render(data);
		return r.ResolveToRgba8();
	}

	/// <summary>For the self-test: the parallel raster resolved through the <em>exact</em> (non-LUT) encode, so a diff
	/// against <see cref="Render"/> isolates raster correctness from the present path's resolve-LUT approximation.</summary>
	public byte[] RenderParallelRgba(int width, int height, double seconds)
	{
		Compose(width, height, seconds);
		var r = _rasterizer!;
		BindImages(r);
		r.Clear(_clear);
		foreach (var (_, data) in _host.DrawLists)
			r.RenderParallel(data);
		return r.ResolveToRgba8();
	}

	private void EnsureSize(int width, int height)
	{
		if (_rasterizer is { } r && r.Width == width && r.Height == height)
			return;
		_rasterizer = new SoftwareRasterizer(width, height);
		_rasterizer.BindTexture(1, _fontPage); // glyph runs sample TextureIndex 1
		_boundImages.Clear();                  // the new rasterizer has no image bindings yet
	}

	// Upload any Image source referenced this frame to its texture index (once per rasterizer instance), so
	// UIPrimKind.Image commands have a texture to sample.
	private void BindImages(SoftwareRasterizer r)
	{
		foreach (var (_, data) in _host.DrawLists)
		{
			foreach (var image in data.Images)
			{
				if (_boundImages.Add(image.TextureIndex))
					r.BindTexture(image.TextureIndex, Texture.FromAtlas(image.Pixels));
			}
		}
	}

	private static double Ms(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
}

/// <summary>Wall-clock cost of one frame's phases, in milliseconds.</summary>
internal readonly record struct FrameTimings(double TickMs, double ComposeMs, double RasterMs, double ResolveMs)
{
	public double TotalMs => TickMs + ComposeMs + RasterMs + ResolveMs;
}
