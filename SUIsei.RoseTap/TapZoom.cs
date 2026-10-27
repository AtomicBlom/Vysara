using System.Globalization;
using System.Numerics;

using SUIsei.Drawing;
using SUIsei.Rasterizer;
using SUIsei.RoutedEvents;

namespace SUIsei.RoseTap;

/// <summary>
/// The magnifier, a toolbar tool with no wire command: scaling the game's UI with a render transform, or a pixel lens
/// that shows the pixels round the pointer, each drawn as a block, and reads the colour under it.
/// <para>
/// The lens reads the frame the game presented where engine glue supplied one through
/// <see cref="Diagnostics.FrameSource"/>, and otherwise the UI alone, rasterized on the CPU from the view's last draw
/// list with SUIsei's own copy of the UI shader: exact UI colours, with none of the scene behind them, which the toolbar
/// says. A capture is taken at most every quarter second, since a rasterization or a readback costs far more than a
/// pointer move. The rasterizer's surface-sized framebuffer is kept only while the lens is on.
/// </para>
/// <para>
/// The pointer is followed without being captured: a handler on each view's content that also hears handled moves, so
/// the lens and the scale's origin follow the pointer over the game while no pick is armed. It is taken off when the
/// magnifier is, so a stopped tap leaves nothing on the game's elements.
/// </para>
/// <para>
/// A tick with nothing new allocates nothing and sets nothing: the transform, the lens and the toolbar's row are redrawn
/// only when the pointer, a capture, the mode or the views have changed, and the row's text only when what it says has.
/// </para>
/// </summary>
internal sealed class TapZoom
{
	private enum Mode { Off, Transform, Lens }

	// How many pixels across the lens shows, and how far from the pointer it is drawn, in surface pixels: far enough that
	// the lens never covers the pixels it samples.
	private const int Samples = 15;
	private const float LensOffset = 32f;
	private static readonly TimeSpan CaptureEvery = TimeSpan.FromMilliseconds(250);

	private readonly Dictionary<UIView, (UIElement Content, RoutedEventHandler Handler)> _watching = [];
	private readonly Dictionary<UIElement, Matrix3x2> _saved = [];
	private readonly byte[] _pixels = new byte[Samples * Samples * 4];

	private Mode _mode;
	private int _factor = 4;
	private TapOverlayView? _pointerView;
	private Vector2 _pointer = new(-1f, -1f);
	private SoftwareRasterizer? _rasterizer;
	private byte[] _resolved = [];
	private DateTime _captured = DateTime.MinValue;
	private bool _fromFrame;
	private bool _hasPixels;

	// What the last draw showed: redrawn when stale, and the row's text rebuilt only when its key moves.
	private bool _stale = true;
	private int _drawnViews;
	private (Mode Mode, int Factor, string? Hex, bool HasPixels, bool FromFrame) _rowKey = (Mode.Off, 0, null, false, false);
	private string _row = "";
	private int _rgb = -1;
	private string _hex = "";

	/// <summary>Whether either mode is on.</summary>
	public bool On => _mode != Mode.Off;

	/// <summary>The colour under the lens as <c>#RRGGBB</c> and where it was read, in surface pixels; null with the lens
	/// off or before it has read anything, since what Ask sends has to be a colour somebody could see.</summary>
	public (string Hex, int X, int Y)? Colour { get; private set; }

	/// <summary>The zoom row's buttons, in order.</summary>
	public IEnumerable<(string Name, Action<TapOverlay> Action)> Actions() =>
	[
		("Scale", overlay => SetMode(overlay, Mode.Transform)),
		("Pixels", overlay => SetMode(overlay, Mode.Lens)),
		("−", overlay => ZoomBy(overlay, -1)),
		("+", overlay => ZoomBy(overlay, 1)),
	];

	/// <summary>The toolbar's Zoom button: the lens on, or the magnifier off.</summary>
	public void Toggle(TapOverlay overlay) => SetMode(overlay, _mode == Mode.Off ? Mode.Lens : Mode.Off);

	/// <summary>Turn the magnifier off, putting the game's transforms back and taking the pointer handlers off.</summary>
	public void Off(TapOverlay overlay) => SetMode(overlay, Mode.Off);

	/// <summary>Where the pointer is, from the capture layer or the passive handler.</summary>
	public void PointerAt(TapOverlayView view, Vector2 point)
	{
		if (ReferenceEquals(view, _pointerView) && point == _pointer)
			return;
		_pointerView = view;
		_pointer = point;
		_stale = true;
	}

	/// <summary>Bring the scale's origin and the lens up to date with the pointer, capturing pixels when the last capture
	/// is old enough. Called each tick while the overlay is up.</summary>
	public void Refresh(TapOverlay overlay)
	{
		if (_mode == Mode.Off)
			return;
		Watch(overlay);
		if (_mode == Mode.Lens && _pointerView is { } view && DateTime.UtcNow - _captured >= CaptureEvery)
		{
			_captured = DateTime.UtcNow;
			Capture(view);
		}
		if (overlay.Views.Count != _drawnViews)
			_stale = true;
		if (!_stale)
			return;
		_stale = false;
		if (_mode == Mode.Transform)
			ApplyTransform(overlay);
		Draw(overlay);
	}

	private void SetMode(TapOverlay overlay, Mode mode)
	{
		if (_mode == mode)
			return;
		if (_mode == Mode.Transform)
			RestoreTransforms();
		if (_mode == Mode.Lens)
		{
			_rasterizer = null;
			_resolved = [];
		}
		_mode = mode;
		Colour = null;
		_hasPixels = false;
		_captured = DateTime.MinValue;
		_stale = true;
		if (mode == Mode.Off)
		{
			Unwatch();
			Draw(overlay);
		}
		else
		{
			Refresh(overlay);
		}
		overlay.Chrome();
	}

	private void ZoomBy(TapOverlay overlay, int step)
	{
		_factor = step > 0 ? Math.Min(32, _factor * 2) : Math.Max(2, _factor / 2);
		_stale = true;
		Refresh(overlay);
	}

	// --- Scaling ---

	// The game's content in each view, scaled about the pointer where it is over that view and about the centre where it
	// is not. The transform each had before is kept and put back.
	private void ApplyTransform(TapOverlay overlay)
	{
		foreach (var view in overlay.Views)
		{
			if (view.View.Root.Content is not { } content)
				continue;
			if (!_saved.ContainsKey(content))
				_saved[content] = content.RenderTransform;
			var size = view.Root.LayoutRect;
			var origin = ReferenceEquals(_pointerView, view) && _pointer.X >= 0f
				? _pointer / view.Scale
				: new Vector2(size.Width / 2f, size.Height / 2f);
			content.RenderTransform = _saved[content] * Matrix3x2.CreateScale(_factor, origin);
			content.InvalidateArrange();
		}
	}

	private void RestoreTransforms()
	{
		foreach (var (content, transform) in _saved)
		{
			content.RenderTransform = transform;
			content.InvalidateArrange();
		}
		_saved.Clear();
	}

	// --- The lens ---

	// The pixels round the pointer: the presented frame where engine glue supplies one, else the view's UI rasterized.
	private void Capture(TapOverlayView view)
	{
		var x0 = (int)_pointer.X - Samples / 2;
		var y0 = (int)_pointer.Y - Samples / 2;
		if (Diagnostics.FrameSource is { } frames && frames.TryReadPixels(x0, y0, Samples, Samples, _pixels))
		{
			_fromFrame = true;
			_hasPixels = true;
		}
		else
		{
			_fromFrame = false;
			_hasPixels = RasterizeUI(view, x0, y0);
		}
		_stale = true;

		if (!_hasPixels)
		{
			Colour = null;
			return;
		}
		var centre = (Samples / 2 * Samples + Samples / 2) * 4;
		var rgb = (_pixels[centre] << 16) | (_pixels[centre + 1] << 8) | _pixels[centre + 2];
		if (rgb != _rgb)
		{
			_rgb = rgb;
			_hex = string.Create(CultureInfo.InvariantCulture, $"#{rgb:X6}");
		}
		Colour = (_hex, (int)_pointer.X, (int)_pointer.Y);
	}

	// The view's last draw list rasterized over the lens's square, into _pixels as RGBA8. False where the view has not
	// been rendered yet.
	private bool RasterizeUI(TapOverlayView view, int x0, int y0)
	{
		DrawListData? list = null;
		var lists = view.Host.DrawLists;
		for (var i = 0; i < lists.Count && list is null; i++)
		{
			if (lists[i].Id == view.View.Surface)
				list = lists[i].Data;
		}
		if (list is null)
			return false;

		var width = Math.Max(1, (int)MathF.Round(view.Root.LayoutRect.Width * view.Scale));
		var height = Math.Max(1, (int)MathF.Round(view.Root.LayoutRect.Height * view.Scale));
		if (_rasterizer is null || _rasterizer.Width != width || _rasterizer.Height != height)
		{
			_rasterizer = new SoftwareRasterizer(width, height);
			_resolved = new byte[width * height * 4];
		}

		// Only the lens's square is drawn and resolved, and only the pixels inside the surface are read back from it.
		var (x1, y1) = (x0 + Samples - 1, y0 + Samples - 1);
		_rasterizer.RenderParallel(list, Vector4.Zero, x0, y0, x1, y1);
		_rasterizer.ResolveToBgra8(_resolved, x0, y0, x1, y1);
		for (var y = 0; y < Samples; y++)
		{
			for (var x = 0; x < Samples; x++)
			{
				var (sx, sy) = (x0 + x, y0 + y);
				var to = (y * Samples + x) * 4;
				if (sx < 0 || sy < 0 || sx >= width || sy >= height)
				{
					_pixels.AsSpan(to, 4).Clear();
					continue;
				}
				var from = (sy * width + sx) * 4;
				(_pixels[to], _pixels[to + 1], _pixels[to + 2], _pixels[to + 3]) =
					(_resolved[from + 2], _resolved[from + 1], _resolved[from], _resolved[from + 3]);
			}
		}
		return true;
	}

	// The zoom row on every view's toolbar, and the lens on the view the pointer is over.
	private void Draw(TapOverlay overlay)
	{
		var key = (_mode, _factor, Colour?.Hex, _hasPixels, _fromFrame);
		var rowChanged = key != _rowKey;
		if (rowChanged)
		{
			_rowKey = key;
			_row = _mode switch
			{
				Mode.Transform => $"Scale {_factor}x",
				Mode.Lens => $"Pixels {_factor}x  {Colour?.Hex ?? "--"}  {(_hasPixels ? _fromFrame ? "frame" : "UI only" : "")}",
				_ => "",
			};
		}
		var swatch = HdrColor.Transparent;
		var viewsChanged = overlay.Views.Count != _drawnViews;
		var hasSwatch = (rowChanged || viewsChanged) && Colour is { } colour && HdrColor.TryParse(colour.Hex, out swatch);

		_drawnViews = overlay.Views.Count;
		foreach (var view in overlay.Views)
		{
			view.ZoomRow.Visibility = _mode == Mode.Off ? Visibility.Collapsed : Visibility.Visible;
			view.ZoomText.Text = _row;
			view.Paint("Scale", _mode == Mode.Transform);
			view.Paint("Pixels", _mode == Mode.Lens);
			view.Paint("−", false, _factor > 2);
			view.Paint("+", false, _factor < 32);
			if (hasSwatch)
				view.Swatch.Background = swatch;

			var showLens = _mode == Mode.Lens && _hasPixels && ReferenceEquals(view, _pointerView);
			view.ShowLens(showLens ? _pointer + new Vector2(LensOffset) : null, Samples, _factor, _pixels);
		}
	}

	// --- Following the pointer ---

	private void Watch(TapOverlay overlay)
	{
		foreach (var view in overlay.Views)
		{
			if (view.View.Root.Content is not { } content)
				continue;
			if (_watching.TryGetValue(view.View, out var watched) && ReferenceEquals(watched.Content, content))
				continue;
			if (watched.Content is not null)
				watched.Content.RemoveHandler(UIElement.PointerMovedEvent, watched.Handler);
			var target = view;
			RoutedEventHandler handler = (_, e) => PointerAt(target, ((PointerRoutedEventArgs)e).Pointer.Position);
			content.AddHandler(UIElement.PointerMovedEvent, handler, handledEventsToo: true);
			_watching[view.View] = (content, handler);
			_stale = true;
		}
	}

	private void Unwatch()
	{
		foreach (var (content, handler) in _watching.Values)
			content.RemoveHandler(UIElement.PointerMovedEvent, handler);
		_watching.Clear();
	}
}
