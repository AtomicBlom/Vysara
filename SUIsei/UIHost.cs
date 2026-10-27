using System.Numerics;
using System.Text;
using SUIsei.Animation;
using SUIsei.Drawing;
using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei;

/// <summary>
/// A per-window UI host — <b>not</b> a singleton, so multi-window/multi-surface is first-class. It owns the input
/// routing and the set of roots outside the render graph, holds no engine or backend handle, and exposes the
/// per-frame split: queue neutral events between frames, then once per frame <see cref="Tick"/> (drain → hit-test →
/// route → lay out) and <see cref="Render"/> (composite each view → <see cref="DrawLists"/>). Both run headless
/// (no GPU), so the whole pipeline is unit-tested by queuing events and asserting DrawLists/DumpState.
/// </summary>
public sealed class UIHost
{
	private readonly List<UIView> _views = [];
	private readonly Queue<PointerInput> _pointerQueue = new();
	private readonly Queue<KeyInput> _keyQueue = new();
	private readonly Queue<string> _textQueue = new();
	private readonly List<PointerInput> _drain = [];
	private readonly KeyRoutedEventArgs _keyArgs = new();
	private readonly CharacterReceivedRoutedEventArgs _charArgs = new();
	private readonly List<(DrawListId Id, DrawListData Data)> _drawLists = [];
	private readonly TextureResidency _textures;
	private int _nextSurface;
	private Size _framebufferSize = new(1f, 1f);

	public UIHost()
	{
		_textures = new TextureResidency(_views);
		// A press focuses the nearest focusable element at or above the hit (e.g. a templated control whose hit
		// target is its non-focusable template root), matching WinUI.
		Input.OnPress = el => Focus.FocusFromPointer(el);
		HotReload.TrackHost(this);
	}

	/// <summary>Pointer routing + over-tracking + press-capture (persistent host state).</summary>
	public InputManager Input { get; } = new();

	/// <summary>Keyboard focus state machine.</summary>
	public FocusManager Focus { get; } = new();

	/// <summary>The animation clock advancing this host's storyboards, stepped each <see cref="Tick"/>.</summary>
	public AnimationClock Clock { get; } = new();

	/// <summary>
	/// True when this host would consume pointer input — a pointer is over drawn (opaque) UI content or a pointer
	/// capture is active. A coarse, per-window capture report in the shape of WinUI/ImGui's <c>WantCaptureMouse</c>:
	/// an input-arbitration layer reads it to decide whether to withhold pointer input from lower (game) layers.
	/// Recomputed each <see cref="Tick"/> from this frame's routed state, so a consumer that reads it between ticks
	/// sees the last tick's result (one frame of coarse latency). It reflects the pointer's last known position, so
	/// the host must feed a move-off (or window-leave) event for it to fall back to false when the pointer leaves.
	/// </summary>
	public bool WantsPointer { get; private set; }

	/// <summary>
	/// True when this host would consume keyboard input — an element holds keyboard focus, so this frame's keys route
	/// into the UI rather than past it. The keyboard twin of <see cref="WantsPointer"/>, shaped like WinUI/ImGui's
	/// <c>WantCaptureKeyboard</c>. Recomputed each <see cref="Tick"/>.
	/// </summary>
	public bool WantsKeyboard { get; private set; }

	/// <summary>The roots this host owns.</summary>
	public IReadOnlyList<UIView> Views => _views;

	/// <summary>The last <see cref="Render"/> output — the draw lists handed to the Loom backend.</summary>
	public IReadOnlyList<(DrawListId Id, DrawListData Data)> DrawLists => _drawLists;

	/// <summary>
	/// The texture sources the last <see cref="Render"/> let go of, for the host to release its textures for: each source
	/// no list in <see cref="DrawLists"/> names any more (an element removed, an image replaced, a view removed, a tree a
	/// hot-reload rebuild replaced, an image disposed since the render before, whose surfaces this render redrew without
	/// it). No list in <see cref="DrawLists"/> samples any of them, so a host can release them before drawing those
	/// lists, deferring destruction past the frames still in flight. Read it after each <see cref="Render"/>: the next one
	/// replaces it. A source drawn again later is new to the host and uploaded again. A host image's
	/// (<see cref="Extensions.HostImageSource"/>) texture is the host's own: it stops binding it rather than destroying it.
	/// A source whose texels changed is not released but replaced, which the host does itself on seeing its
	/// <see cref="TextureSource.Version"/> move. <see cref="TextureSource.White"/> is never released.
	/// <para>
	/// It covers the lists this host hands out. A view composited by a foreign <see cref="IUICompositor"/> hands out
	/// none, so that compositor tracks what it draws itself, and reads <see cref="TextureSource.IsDisposed"/>.
	/// </para>
	/// </summary>
	public IReadOnlyList<TextureSource> ReleasedTextures => _textures.Released;

	/// <summary>Add a root at <paramref name="layer"/>; returns the handle to keep. The first overlay gets
	/// <see cref="UISurface.Hud"/> (list 0); subsequent views take higher ids. Pass <paramref name="compositor"/> to
	/// use a non-default compositor, such as a foreign engine's. The root's text font is <see cref="UIRoot.Font"/>.</summary>
	public UIView CreateRoot(in UILayer layer, bool interactive = true, IUICompositor? compositor = null)
	{
		var root = new UIRoot(new DrawListId(_nextSurface++), compositor)
		{
			LayoutScale = layer.LayoutScale,
			InputManager = Input,
			Clock = Clock
		};
		var view = new UIView(root, layer, interactive);
		_views.Add(view);
		return view;
	}

	/// <summary>Remove a root. The texture sources it drew are let go of at the next <see cref="Render"/>
	/// (<see cref="ReleasedTextures"/>).</summary>
	public void RemoveRoot(UIView view)
	{
		if (_views.Remove(view))
			view.Textures.Clear(_textures);
	}

	/// <summary>Queue a neutral pointer event for the next <see cref="Tick"/>.</summary>
	public void QueuePointer(in PointerInput e) => _pointerQueue.Enqueue(e);

	/// <summary>Queue a keyboard key event for the next <see cref="Tick"/>.</summary>
	public void QueueKey(in KeyInput e) => _keyQueue.Enqueue(e);

	/// <summary>Queue a text string for the next <see cref="Tick"/> (translates to
	/// <see cref="UIElement.CharacterReceivedEvent"/> on the focused element).</summary>
	public void QueueText(ReadOnlySpan<char> utf16) => _textQueue.Enqueue(utf16.ToString());

	/// <summary>
	/// Once per frame: lay out each root, release focus from a control that has been disabled, drain pointer queue
	/// (coalescing moves) → hit-test → route events, then drain keyboard queue → Tab intercepts focus → remaining keys
	/// route to the focused element. Layout runs first so input hit-tests this frame's geometry.
	/// </summary>
	public void Tick(double deltaSeconds, float time, Vector2 framebufferSize)
	{
		// Apply any queued XAML Hot Reload rebuilds before layout, so an edited view lays out this frame. Inert
		// unless hot reload is active (HotReload.Enabled).
		HotReload.ProcessPending();

		// Advance animations FIRST: a clock write to a layout-affecting DP invalidates measure/arrange so the new
		// value is reflected in this frame's LayoutPass; a visual-channel write (transform/opacity/brush) only marks
		// the subtree dirty for the composite, with no relayout.
		Clock.Advance(deltaSeconds);

		_framebufferSize = new Size(framebufferSize.X, framebufferSize.Y);
		foreach (var view in _views)
			view.Root.LayoutPass(_framebufferSize);
		Focus.ReleaseDisabledFocus();
		DrainPointers();
		DrainKeyboard();

		// Resolve the coarse capture report after routing/focus settle, so it is O(1) reads of this frame's state.
		WantsPointer = Input.WantsPointer;
		WantsKeyboard = Focus.WantsKeyboard;
	}

	/// <summary>Composite each view's visual tree → <see cref="DrawLists"/>, and report the texture sources no list
	/// samples any more → <see cref="ReleasedTextures"/>.</summary>
	public void Render()
	{
		_textures.BeginRender();
		_drawLists.Clear();
		foreach (var view in _views)
		{
			var list = view.Root.Render();
			_drawLists.Add((view.Surface, list));
			view.Textures.Track(list, _textures);
		}
		_textures.EndRender();
	}

	/// <summary>Focus / capture / over-chains for input + focus tests.</summary>
	public string DumpState()
	{
		var sb = new StringBuilder();
		Focus.DumpState(sb);
		Input.DumpState(sb);
		return sb.ToString();
	}

	/// <summary>The texture sources this host's surfaces hold, each with how many surfaces draw it, and those the last
	/// <see cref="Render"/> released.</summary>
	public string DumpTextures()
	{
		var sb = new StringBuilder();
		_textures.Dump(sb);
		return sb.ToString();
	}

	private void DrainPointers()
	{
		if (_pointerQueue.Count == 0)
			return;
		_drain.Clear();
		while (_pointerQueue.Count > 0)
			_drain.Add(_pointerQueue.Dequeue());

		for (var i = 0; i < _drain.Count; i++)
		{
			var e = _drain[i];
			if (e.Update == PointerUpdateKind.None && i + 1 < _drain.Count)
			{
				var n = _drain[i + 1];
				if (n.Update == PointerUpdateKind.None && n.PointerId == e.PointerId)
					continue; // coalesce a run of moves to the latest
			}
			Input.Process(e, _views);
		}
	}

	private void DrainKeyboard()
	{
		while (_keyQueue.Count > 0)
		{
			var e = _keyQueue.Dequeue();

			// Tab / Shift-Tab: intercept before the focused element sees the key.
			if (e is { Down: true, Key: VirtualKey.Tab })
			{
				var dir = (e.Modifiers & ModifierKeys.Shift) != 0
					? FocusNavigationDirection.Previous
					: FocusNavigationDirection.Next;
				Focus.MoveFocus(dir, _views);
				continue;
			}

			var focused = Focus.FocusedElement;
			if (focused is null)
				continue;

			// Tunnel Preview → if unhandled → bubble main event.
			var previewEvt = e.Down ? UIElement.PreviewKeyDownEvent : UIElement.PreviewKeyUpEvent;
			var mainEvt = e.Down ? UIElement.KeyDownEvent : UIElement.KeyUpEvent;

			_keyArgs.Reset(previewEvt, focused, e);
			focused.RaiseEvent(previewEvt, _keyArgs);
			if (_keyArgs.Handled)
				continue;

			_keyArgs.Reset(mainEvt, focused, e);
			focused.RaiseEvent(mainEvt, _keyArgs);
		}

		var focusedForText = Focus.FocusedElement;
		if (focusedForText is null)
		{
			_textQueue.Clear();
			return;
		}

		while (_textQueue.Count > 0)
		{
			foreach (var ch in _textQueue.Dequeue())
			{
				_charArgs.Reset(UIElement.CharacterReceivedEvent, focusedForText, ch);
				focusedForText.RaiseEvent(UIElement.CharacterReceivedEvent, _charArgs);
			}
		}
	}
}
