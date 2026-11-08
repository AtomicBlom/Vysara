using System.Numerics;
using System.Runtime.InteropServices;
using SUIsei.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SUIsei.Gallery;

/// <summary>
/// A single top-level Win32 window that presents a <see cref="GalleryRenderer"/> with GDI — no GPU. Each frame the
/// renderer composites + software-rasterizes the control tree to straight RGBA8; this swizzles it to BGRA and pushes
/// it to the window with <c>StretchDIBits</c>. Win32 messages translate into SUIsei's device-neutral pointer/key
/// events. Reached entirely through CsWin32-generated P/Invokes, so there is no hand-written interop.
/// </summary>
internal sealed unsafe class Win32Window
{
	private const string ClassName = "SUIseiGalleryWindow";
	private const WINDOW_STYLE WindowStyle = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;

	// The window owns the only renderer, so a single static back-reference lets the unmanaged WndProc thunk reach it.
	private static Win32Window? s_instance;
	// Rooted so the GC never collects the thunk Win32 holds a raw pointer to for the window's lifetime.
	private static WNDPROC? s_wndProc;

	private readonly GalleryRenderer _renderer;
	private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

	private HWND _hwnd;
	private int _width;
	private int _height;
	private byte[] _bgra = []; // GDI 32bpp top-down DIB scratch (B,G,R,A per pixel)
	private bool _running = true;
	private long _lastKey = long.MinValue; // last presented frame's damage key (skip re-raster when unchanged)
	private bool _forcePresent = true;     // set when the OS invalidates the window (paint/resize/first frame)
	private Vector2 _lastMousePos;         // last WM_MOUSEMOVE in client pixels — where a wheel event hit-tests

	/// <summary>A window showing the gallery's shell, opened at <paramref name="page"/> (by its key), or at its first page.</summary>
	public Win32Window(string? page) => _renderer = new GalleryRenderer(new GalleryShell(page) { Quit = Quit });

	/// <summary>Register the window class, create the window sized for its monitor's DPI, and show it.
	/// <paramref name="width"/> and <paramref name="height"/> are the desired client size in DIPs.</summary>
	public void Create(int width, int height, string title)
	{
		s_instance = this;
		var hInstance = (HINSTANCE)Marshal.GetHINSTANCE(typeof(Win32Window).Module);

		s_wndProc = WndProcThunk;
		fixed (char* className = ClassName)
		{
			var wc = new WNDCLASSEXW
			{
				cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
				style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
				lpfnWndProc = s_wndProc,
				hInstance = hInstance,
				hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
				hbrBackground = HBRUSH.Null, // no background erase — every pixel comes from the blit, so no flicker
				lpszClassName = new PCWSTR(className),
			};
			if (PInvoke.RegisterClassEx(in wc) == 0)
				throw new InvalidOperationException($"RegisterClassEx failed (0x{Marshal.GetLastPInvokeError():X8}).");
		}

		_hwnd = PInvoke.CreateWindowEx(
			default, ClassName, title,
			WindowStyle | WINDOW_STYLE.WS_VISIBLE,
			PInvoke.CW_USEDEFAULT, PInvoke.CW_USEDEFAULT, width, height,
			HWND.Null, HMENU.Null, hInstance, null);
		if (_hwnd == HWND.Null)
			throw new InvalidOperationException($"CreateWindowEx failed (0x{Marshal.GetLastPInvokeError():X8}).");

		// The provisional CreateWindowEx size ignores DPI; resize so the client area is the requested DIP size at the
		// monitor's actual scale, and set the UI's DIP→pixel scale to match.
		ApplyDpiScale(PInvoke.GetDpiForWindow(_hwnd), width, height);

		PInvoke.GetClientRect(_hwnd, out var rc);
		_width = rc.right - rc.left;
		_height = rc.bottom - rc.top;

		PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOW);
		PInvoke.UpdateWindow(_hwnd);
	}

	// Set the UI scale from the DPI and resize the window so its client area is clientDipW × clientDipH DIPs at that
	// scale. AdjustWindowRectExForDpi expands the client size to the full window size (frame + caption) for this DPI.
	private void ApplyDpiScale(uint dpi, int clientDipW, int clientDipH)
	{
		_renderer.Scale = dpi / 96f;
		var rc = new RECT
		{
			right = (int)MathF.Round(clientDipW * dpi / 96f),
			bottom = (int)MathF.Round(clientDipH * dpi / 96f),
		};
		PInvoke.AdjustWindowRectExForDpi(ref rc, WindowStyle, false, default, dpi);
		PInvoke.SetWindowPos(_hwnd, HWND.Null, 0, 0, rc.right - rc.left, rc.bottom - rc.top,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
	}

	/// <summary>Pump messages and repaint until the window closes. Non-blocking pump so animations keep ticking.</summary>
	public void RunLoop()
	{
		while (_running)
		{
			while (PInvoke.PeekMessage(out var msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
			{
				if (msg.message == PInvoke.WM_QUIT)
				{
					_running = false;
					break;
				}
				PInvoke.TranslateMessage(in msg);
				PInvoke.DispatchMessage(in msg);
			}
			if (!_running)
				break;

			RenderFrame();
			Thread.Sleep(8); // ~120 Hz ceiling; the software pass is far cheaper, so this just yields the CPU
		}
	}

	private void Quit()
	{
		if (_hwnd != HWND.Null)
			PInvoke.DestroyWindow(_hwnd);
	}

	private static LRESULT WndProcThunk(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam) =>
		s_instance?.WndProc(hwnd, msg, wParam, lParam) ?? PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);

	private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
	{
		switch (msg)
		{
			case PInvoke.WM_DESTROY:
				PInvoke.PostQuitMessage(0);
				return default;

			case PInvoke.WM_SIZE:
				_width = LoWord(lParam);
				_height = HiWord(lParam);
				return default;

			case PInvoke.WM_DPICHANGED:
				{
					// LOWORD(wParam) is the new DPI; lParam points at the window rect Windows suggests to keep the same
					// DIP size on the new monitor. Adopt both — the follow-up WM_SIZE updates the framebuffer dimensions.
					_renderer.Scale = ((uint)wParam.Value & 0xFFFF) / 96f;
					var suggested = (RECT*)lParam.Value;
					PInvoke.SetWindowPos(hwnd, HWND.Null,
						suggested->left, suggested->top,
						suggested->right - suggested->left, suggested->bottom - suggested->top,
						SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
					return default;
				}

			case PInvoke.WM_PAINT:
				_forcePresent = true; // the client area was invalidated — present even if nothing changed
				RenderFrame();
				PInvoke.ValidateRect(hwnd, (RECT*)null);
				return default;

			case PInvoke.WM_MOUSEMOVE:
				_lastMousePos = MousePos(lParam);
				_renderer.QueuePointer(PointerInput.Move(_lastMousePos, ButtonsOf(wParam)));
				return default;

			case PInvoke.WM_LBUTTONDOWN:
				_renderer.QueuePointer(PointerInput.Press(MousePos(lParam)));
				return default;
			case PInvoke.WM_LBUTTONUP:
				_renderer.QueuePointer(PointerInput.Release(MousePos(lParam)));
				return default;
			case PInvoke.WM_RBUTTONDOWN:
				_renderer.QueuePointer(PointerInput.Press(MousePos(lParam), PointerUpdateKind.RightButtonPressed, PointerButtons.Right));
				return default;
			case PInvoke.WM_RBUTTONUP:
				_renderer.QueuePointer(PointerInput.Release(MousePos(lParam), PointerUpdateKind.RightButtonReleased));
				return default;

			case PInvoke.WM_MOUSEWHEEL:
				{
					// WHEEL_DELTA notches in the signed high word of wParam (forward/up is positive). lParam carries screen
					// coordinates here, not client; rather than convert, hit-test at the last cursor position (the wheel
					// targets whatever the pointer is over). One notch is one wheel unit — the ScrollViewer scales it.
					var notches = (short)((ulong)wParam.Value >> 16) / 120f;
					_renderer.QueuePointer(PointerInput.Scroll(_lastMousePos, new Vector2(0f, notches)));
					return default;
				}

			case PInvoke.WM_KEYDOWN:
				_renderer.QueueKey(new KeyInput((VirtualKey)(uint)wParam.Value, Down: true, Repeat: false, Modifiers(), 0));
				return default;
			case PInvoke.WM_KEYUP:
				_renderer.QueueKey(new KeyInput((VirtualKey)(uint)wParam.Value, Down: false, Repeat: false, Modifiers(), 0));
				return default;

			case PInvoke.WM_CHAR:
				var ch = (char)(ushort)wParam.Value;
				if (!char.IsControl(ch))
					_renderer.QueueText([ch]);
				return default;
		}
		return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
	}

	private void RenderFrame()
	{
		if (_width <= 0 || _height <= 0)
			return;

		var key = _renderer.Compose(_width, _height, _clock.Elapsed.TotalSeconds);

		var bytes = _width * _height * 4;
		if (_bgra.Length != bytes)
		{
			_bgra = new byte[bytes];
			_forcePresent = true; // buffer resized — the old contents are stale
		}

		// Nothing changed since the last present and the window is still valid: leave the last frame on screen.
		if (!_forcePresent && key == _lastKey)
			return;
		var full = _forcePresent; // resize / OS repaint → repaint the whole client; otherwise just the damage rect
		_lastKey = key;
		_forcePresent = false;

		// Re-raster + resolve only the changed region into the DIB; the rest of _bgra still holds the last frame, so a
		// (cheap) full blit of the buffer shows the correct whole frame.
		_renderer.PresentTo(_bgra, full);

		var hdc = PInvoke.GetDC(_hwnd);
		Present(hdc);
		PInvoke.ReleaseDC(_hwnd, hdc);
	}

	private void Present(HDC hdc)
	{
		var bmi = new BITMAPINFO();
		bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
		bmi.bmiHeader.biWidth = _width;
		bmi.bmiHeader.biHeight = -_height; // negative ⇒ top-down (row 0 is the top), matching the rasterizer
		bmi.bmiHeader.biPlanes = 1;
		bmi.bmiHeader.biBitCount = 32;
		bmi.bmiHeader.biCompression = 0u; // BI_RGB — uncompressed

		fixed (byte* bits = _bgra)
		{
			PInvoke.StretchDIBits(
				hdc,
				0, 0, _width, _height,
				0, 0, _width, _height,
				bits, &bmi, DIB_USAGE.DIB_RGB_COLORS, ROP_CODE.SRCCOPY);
		}
	}

	private static int LoWord(LPARAM l) => (short)(l.Value & 0xFFFF);
	private static int HiWord(LPARAM l) => (short)((l.Value >> 16) & 0xFFFF);
	private static Vector2 MousePos(LPARAM l) => new(LoWord(l), HiWord(l));

	private static PointerButtons ButtonsOf(WPARAM w)
	{
		var v = (nuint)w.Value;
		var b = PointerButtons.None;
		if ((v & 0x0001) != 0) b |= PointerButtons.Left;   // MK_LBUTTON
		if ((v & 0x0002) != 0) b |= PointerButtons.Right;  // MK_RBUTTON
		if ((v & 0x0010) != 0) b |= PointerButtons.Middle; // MK_MBUTTON
		return b;
	}

	private static ModifierKeys Modifiers()
	{
		var m = ModifierKeys.None;
		if ((PInvoke.GetKeyState((int)VirtualKey.Shift) & 0x8000) != 0) m |= ModifierKeys.Shift;
		if ((PInvoke.GetKeyState((int)VirtualKey.Control) & 0x8000) != 0) m |= ModifierKeys.Control;
		if ((PInvoke.GetKeyState((int)VirtualKey.Alt) & 0x8000) != 0) m |= ModifierKeys.Alt;
		return m;
	}
}
