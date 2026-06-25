using System.Numerics;
using System.Text;
using SUIsei.Drawing;
using SUIsei.Input;

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
	private readonly List<UIView>        _views        = [];
	private readonly Queue<PointerInput> _pointerQueue = new();
	private readonly Queue<KeyInput>     _keyQueue     = new();
	private readonly Queue<string>       _textQueue    = new();
	private readonly List<PointerInput>  _drain        = [];
	private readonly KeyRoutedEventArgs               _keyArgs  = new();
	private readonly CharacterReceivedRoutedEventArgs _charArgs = new();
	private readonly List<(DrawListId Id, DrawListData Data)> _drawLists = [];
	private int  _nextSurface;
	private Size _framebufferSize = new(1f, 1f);

	public UIHost()
	{
		// A press focuses the nearest focusable element at or above the hit (e.g. a templated control whose hit
		// target is its non-focusable template root), matching WinUI.
		Input.OnPress = el => Focus.FocusFromPointer(el);
		HotReload.TrackHost(this);
	}

	/// <summary>Pointer routing + over-tracking + press-capture (persistent host state).</summary>
	public InputManager Input { get; } = new();

	/// <summary>Keyboard focus state machine.</summary>
	public FocusManager Focus { get; } = new();

	/// <summary>The animation clock advancing this host's storyboards (PlanningUI.md §D5.7), stepped each <see cref="Tick"/>.</summary>
	public AnimationClock Clock { get; } = new();

	/// <summary>The roots this host owns.</summary>
	public IReadOnlyList<UIView> Views => _views;

	/// <summary>The last <see cref="Render"/> output — the draw lists handed to the Loom backend.</summary>
	public IReadOnlyList<(DrawListId Id, DrawListData Data)> DrawLists => _drawLists;

	/// <summary>Add a root at <paramref name="placement"/>; returns the handle to keep. The first overlay gets
	/// <see cref="UISurface.Hud"/> (list 0); subsequent views take higher ids. Pass <paramref name="compositor"/> to
	/// use a non-default compositor — e.g. one bound to a specific (glyph-id / shaped) <see cref="FontAtlas"/>.</summary>
	public UIView CreateRoot(in UIPlacement placement, bool interactive = true, IUICompositor? compositor = null)
	{
		var root = new UIRoot(new DrawListId(_nextSurface++), compositor)
		{
			LayoutScale  = placement.LayoutScale,
			InputManager = Input,
			Clock        = Clock
		};
		var view = new UIView(root, placement, interactive);
		_views.Add(view);
		return view;
	}

	public void RemoveRoot(UIView view) => _views.Remove(view);

	/// <summary>Queue a neutral pointer event for the next <see cref="Tick"/>.</summary>
	public void QueuePointer(in PointerInput e) => _pointerQueue.Enqueue(e);

	/// <summary>Queue a keyboard key event for the next <see cref="Tick"/>.</summary>
	public void QueueKey(in KeyInput e) => _keyQueue.Enqueue(e);

	/// <summary>Queue a text string for the next <see cref="Tick"/> (translates to
	/// <see cref="UIElement.CharacterReceivedEvent"/> on the focused element).</summary>
	public void QueueText(ReadOnlySpan<char> utf16) => _textQueue.Enqueue(utf16.ToString());

	/// <summary>
	/// Once per frame: lay out each root, drain pointer queue (coalescing moves) → hit-test → route events,
	/// then drain keyboard queue → Tab intercepts focus → remaining keys route to the focused element.
	/// Layout runs first so input hit-tests this frame's geometry.
	/// </summary>
	public void Tick(double deltaSeconds, float time, Vector2 framebufferSize)
	{
		// Apply any queued XAML Hot Reload rebuilds before layout, so an edited view lays out this frame. Inert
		// unless hot reload is active (HotReload.Enabled).
		HotReload.ProcessPending();

		// Advance animations FIRST: a clock write to a layout-affecting DP invalidates measure/arrange so the new
		// value is reflected in this frame's LayoutPass; a visual-channel write (transform/opacity/brush) only marks
		// the subtree dirty for the composite, with no relayout (PlanningUI.md §D5.7).
		Clock.Advance(deltaSeconds);

		_framebufferSize = new Size(framebufferSize.X, framebufferSize.Y);
		foreach (var view in _views)
			view.Root.LayoutPass(_framebufferSize);
		DrainPointers();
		DrainKeyboard();
	}

	/// <summary>Composite each view's visual tree → <see cref="DrawLists"/>.</summary>
	public void Render()
	{
		_drawLists.Clear();
		foreach (var view in _views)
			_drawLists.Add((view.Surface, view.Root.Render()));
	}

	/// <summary>Focus / capture / over-chains for input + focus tests.</summary>
	public string DumpState()
	{
		var sb = new StringBuilder();
		Focus.DumpState(sb);
		Input.DumpState(sb);
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
			var mainEvt    = e.Down ? UIElement.KeyDownEvent        : UIElement.KeyUpEvent;

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
