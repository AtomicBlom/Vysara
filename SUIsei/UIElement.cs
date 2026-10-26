using System.Numerics;
using SUIsei.Controls;
using SUIsei.Extensions;
using SUIsei.RoutedEvents;

namespace SUIsei;

/// <summary>
/// The base of every visual element: two-pass measure/arrange, layout invalidation, the
/// owned composition <see cref="Visual"/>, render-affecting DPs, routed events (pointer + keyboard + focus),
/// hit-testing, and focus/capture queries. The WinUI <c>MeasureCore</c>/<c>ArrangeCore</c> seam lets
/// <see cref="FrameworkElement"/> layer Margin/Min/Max/alignment on top of the element's intrinsic sizing.
/// </summary>
public abstract class UIElement : DependencyObject
{
	// --- Dependency properties ---

	public static readonly DependencyProperty<UIElement, float> OpacityProperty =
		DependencyProperty.Register(nameof(Opacity),
			new PropertyMetadata<UIElement, float> { DefaultValue = 1f, Invalidates = InvalidationFlags.Render });

	public static readonly DependencyProperty<UIElement, Visibility> VisibilityProperty =
		DependencyProperty.Register(nameof(Visibility),
			new PropertyMetadata<UIElement, Visibility> { DefaultValue = Visibility.Visible, Invalidates = InvalidationFlags.Measure });

	public static readonly DependencyProperty<UIElement, bool> IsHitTestVisibleProperty =
		DependencyProperty.Register(nameof(IsHitTestVisible),
			new PropertyMetadata<UIElement, bool> { DefaultValue = true });

	/// <summary>Whether this element participates in Tab/keyboard focus navigation. Default false; overridden to
	/// true by <see cref="Controls.Control"/>.</summary>
	public static readonly DependencyProperty<UIElement, bool> IsTabStopProperty =
		DependencyProperty.Register(nameof(IsTabStop),
			new PropertyMetadata<UIElement, bool> { DefaultValue = false });

	/// <summary>Tab-order index. Lower values tab first; equal values use document order.</summary>
	public static readonly DependencyProperty<UIElement, int> TabIndexProperty =
		DependencyProperty.Register(nameof(TabIndex),
			new PropertyMetadata<UIElement, int> { DefaultValue = int.MaxValue });

	/// <summary>Group opacity (animatable — drives the composition <see cref="Visual.Opacity"/>).</summary>
	public float Opacity { get => GetValue(OpacityProperty); set => SetValue(OpacityProperty, value); }

	/// <summary><see cref="Visibility.Collapsed"/> contributes zero to layout and never hit-tests.</summary>
	public Visibility Visibility { get => GetValue(VisibilityProperty); set => SetValue(VisibilityProperty, value); }

	/// <summary>When false, this element (and its subtree, by the walk) is skipped by hit-testing.</summary>
	public bool IsHitTestVisible { get => GetValue(IsHitTestVisibleProperty); set => SetValue(IsHitTestVisibleProperty, value); }

	public bool IsTabStop { get => GetValue(IsTabStopProperty); set => SetValue(IsTabStopProperty, value); }
	public int TabIndex { get => GetValue(TabIndexProperty); set => SetValue(TabIndexProperty, value); }

	// --- Pointer routed events ---
	// Preview events tunnel root→target; the paired bubble events fire after if Preview is unhandled.

	public static readonly RoutedEvent PreviewPointerPressedEvent = RoutedEvent.RegisterInput(nameof(PreviewPointerPressed), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent PreviewPointerReleasedEvent = RoutedEvent.RegisterInput(nameof(PreviewPointerReleased), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent PreviewPointerMovedEvent = RoutedEvent.RegisterInput(nameof(PreviewPointerMoved), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent PointerPressedEvent = RoutedEvent.RegisterInput(nameof(PointerPressed), RoutingStrategy.Bubble);
	public static readonly RoutedEvent PointerReleasedEvent = RoutedEvent.RegisterInput(nameof(PointerReleased), RoutingStrategy.Bubble);
	public static readonly RoutedEvent PointerMovedEvent = RoutedEvent.RegisterInput(nameof(PointerMoved), RoutingStrategy.Bubble);
	public static readonly RoutedEvent PointerEnteredEvent = RoutedEvent.RegisterInput(nameof(PointerEntered), RoutingStrategy.Direct);
	public static readonly RoutedEvent PointerExitedEvent = RoutedEvent.RegisterInput(nameof(PointerExited), RoutingStrategy.Direct);
	public static readonly RoutedEvent PointerCaptureLostEvent = RoutedEvent.RegisterInput(nameof(PointerCaptureLost), RoutingStrategy.Direct);
	public static readonly RoutedEvent PreviewPointerWheelChangedEvent = RoutedEvent.RegisterInput(nameof(PreviewPointerWheelChanged), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent PointerWheelChangedEvent = RoutedEvent.RegisterInput(nameof(PointerWheelChanged), RoutingStrategy.Bubble);

	public event RoutedEventHandler PreviewPointerPressed { add => AddHandler(PreviewPointerPressedEvent, value); remove => RemoveHandler(PreviewPointerPressedEvent, value); }
	public event RoutedEventHandler PreviewPointerReleased { add => AddHandler(PreviewPointerReleasedEvent, value); remove => RemoveHandler(PreviewPointerReleasedEvent, value); }
	public event RoutedEventHandler PreviewPointerMoved { add => AddHandler(PreviewPointerMovedEvent, value); remove => RemoveHandler(PreviewPointerMovedEvent, value); }
	public event RoutedEventHandler PointerPressed { add => AddHandler(PointerPressedEvent, value); remove => RemoveHandler(PointerPressedEvent, value); }
	public event RoutedEventHandler PointerReleased { add => AddHandler(PointerReleasedEvent, value); remove => RemoveHandler(PointerReleasedEvent, value); }
	public event RoutedEventHandler PointerMoved { add => AddHandler(PointerMovedEvent, value); remove => RemoveHandler(PointerMovedEvent, value); }
	public event RoutedEventHandler PointerEntered { add => AddHandler(PointerEnteredEvent, value); remove => RemoveHandler(PointerEnteredEvent, value); }
	public event RoutedEventHandler PointerExited { add => AddHandler(PointerExitedEvent, value); remove => RemoveHandler(PointerExitedEvent, value); }
	public event RoutedEventHandler PointerCaptureLost { add => AddHandler(PointerCaptureLostEvent, value); remove => RemoveHandler(PointerCaptureLostEvent, value); }
	public event RoutedEventHandler PreviewPointerWheelChanged { add => AddHandler(PreviewPointerWheelChangedEvent, value); remove => RemoveHandler(PreviewPointerWheelChangedEvent, value); }
	public event RoutedEventHandler PointerWheelChanged { add => AddHandler(PointerWheelChangedEvent, value); remove => RemoveHandler(PointerWheelChangedEvent, value); }

	// --- Keyboard routed events ---

	public static readonly RoutedEvent PreviewKeyDownEvent = RoutedEvent.RegisterInput(nameof(PreviewKeyDown), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent PreviewKeyUpEvent = RoutedEvent.RegisterInput(nameof(PreviewKeyUp), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent KeyDownEvent = RoutedEvent.RegisterInput(nameof(KeyDown), RoutingStrategy.Bubble);
	public static readonly RoutedEvent KeyUpEvent = RoutedEvent.RegisterInput(nameof(KeyUp), RoutingStrategy.Bubble);
	public static readonly RoutedEvent CharacterReceivedEvent = RoutedEvent.RegisterInput(nameof(CharacterReceived), RoutingStrategy.Bubble);

	public event RoutedEventHandler PreviewKeyDown { add => AddHandler(PreviewKeyDownEvent, value); remove => RemoveHandler(PreviewKeyDownEvent, value); }
	public event RoutedEventHandler PreviewKeyUp { add => AddHandler(PreviewKeyUpEvent, value); remove => RemoveHandler(PreviewKeyUpEvent, value); }
	public event RoutedEventHandler KeyDown { add => AddHandler(KeyDownEvent, value); remove => RemoveHandler(KeyDownEvent, value); }
	public event RoutedEventHandler KeyUp { add => AddHandler(KeyUpEvent, value); remove => RemoveHandler(KeyUpEvent, value); }
	public event RoutedEventHandler CharacterReceived { add => AddHandler(CharacterReceivedEvent, value); remove => RemoveHandler(CharacterReceivedEvent, value); }

	// --- Focus routed events ---

	public static readonly RoutedEvent GettingFocusEvent = RoutedEvent.Register(nameof(GettingFocus), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent LosingFocusEvent = RoutedEvent.Register(nameof(LosingFocus), RoutingStrategy.Tunnel);
	public static readonly RoutedEvent GotFocusEvent = RoutedEvent.Register(nameof(GotFocus), RoutingStrategy.Bubble);
	public static readonly RoutedEvent LostFocusEvent = RoutedEvent.Register(nameof(LostFocus), RoutingStrategy.Bubble);

	public event RoutedEventHandler GettingFocus { add => AddHandler(GettingFocusEvent, value); remove => RemoveHandler(GettingFocusEvent, value); }
	public event RoutedEventHandler LosingFocus { add => AddHandler(LosingFocusEvent, value); remove => RemoveHandler(LosingFocusEvent, value); }
	public event RoutedEventHandler GotFocus { add => AddHandler(GotFocusEvent, value); remove => RemoveHandler(GotFocusEvent, value); }
	public event RoutedEventHandler LostFocus { add => AddHandler(LostFocusEvent, value); remove => RemoveHandler(LostFocusEvent, value); }

	/// <summary>A local render transform, composed under layout placement (animatable later).</summary>
	public Matrix3x2 RenderTransform { get; set; } = Matrix3x2.Identity;

	/// <summary>Whether this element always clips its subtree to its layout bounds, as a control that needs it
	/// (ScrollViewer, TextBox) sets it. Markup asks for the same with <see cref="Clipping.ClipToBoundsProperty"/>; the
	/// clip applies when either is set (in <see cref="SyncVisual"/>).</summary>
	internal bool ClipToBounds { get; set; }

	/// <summary>The result of the last <see cref="Measure"/> (intrinsic + margin), in DIPs.</summary>
	public Size DesiredSize { get; protected set; }

	/// <summary>The final placement from the last <see cref="Arrange"/>, in parent space.</summary>
	public Rect LayoutRect { get; protected set; }

	internal bool MeasureDirty = true;
	internal bool ArrangeDirty = true;
	private Size? _lastAvailable;
	private Rect? _lastFinal;

	internal UIElement? Parent { get; set; }

	/// <summary>The UIRoot this element is currently attached to (null when detached). Set by UIRoot when Content
	/// is assigned and propagated through AdoptChild/DropChild. Provides access to the InputManager for capture.</summary>
	internal UIRoot? AttachedRoot { get; private set; }

	internal void SetAttachedRoot(UIRoot? root)
	{
		AttachedRoot = root;
		foreach (var child in VisualChildren)
			child.SetAttachedRoot(root);
	}

	/// <summary>The composition node this element owns. Layout writes its transform/size;
	/// property setters write its opacity/brush. The retained visual tree mirrors the element tree.</summary>
	internal SpriteVisual Visual { get; } = new();

	/// <summary>The children that participate in layout + visual sync (panels return their children; content
	/// controls/borders return their single child; leaves return empty).</summary>
	internal virtual IReadOnlyList<UIElement> VisualChildren => [];

	// --- Two-pass layout (WinUI semantics) ---

	/// <summary>Pass 1: compute <see cref="DesiredSize"/> for the given available space. Cached when clean.</summary>
	public void Measure(Size available)
	{
		if (!MeasureDirty && _lastAvailable == available)
			return;
		_lastAvailable = available;
		DesiredSize = Visibility == Visibility.Collapsed ? Size.Zero : MeasureCore(available);
		MeasureDirty = false;
		ArrangeDirty = true;
	}

	/// <summary>Pass 2: place the element into <paramref name="finalRect"/> (parent space) and sync its visual.</summary>
	public void Arrange(Rect finalRect)
	{
		if (!ArrangeDirty && _lastFinal == finalRect)
			return;
		_lastFinal = finalRect;
		if (Visibility == Visibility.Collapsed)
			LayoutRect = new Rect(finalRect.X, finalRect.Y, 0, 0);
		else
			ArrangeCore(finalRect);
		ArrangeDirty = false;
		SyncVisual();
	}

	/// <summary>Intrinsic measure hook (Margin/Min/Max applied by <see cref="FrameworkElement"/> around this).</summary>
	protected virtual Size MeasureCore(Size available) => MeasureOverride(available);

	/// <summary>Intrinsic arrange hook — sets <see cref="LayoutRect"/> and arranges children.</summary>
	protected virtual void ArrangeCore(Rect finalRect)
	{
		LayoutRect = finalRect;
		ArrangeOverride(new Size(finalRect.Width, finalRect.Height));
	}

	/// <summary>Per-control measure: return the desired size for the content given <paramref name="available"/>.</summary>
	protected virtual Size MeasureOverride(Size available) => Size.Zero;

	/// <summary>Per-control arrange: place children within <paramref name="finalSize"/> (origin 0,0); return used size.</summary>
	protected virtual Size ArrangeOverride(Size finalSize) => finalSize;

	// --- Invalidation ---

	public void InvalidateMeasure()
	{
		if (MeasureDirty)
			return;
		MeasureDirty = true;
		ArrangeDirty = true;
		_lastAvailable = null;
		Parent?.InvalidateMeasure();
	}

	public void InvalidateArrange()
	{
		if (ArrangeDirty)
			return;
		ArrangeDirty = true;
		_lastFinal = null;
		Parent?.InvalidateArrange();
	}

	/// <summary>A render-only change (opacity, brush): refresh the composition node's render state and mark it dirty
	/// for the next composite — no relayout. Layout-driven sync happens in <see cref="SyncVisual"/> after arrange.</summary>
	public void InvalidateVisual()
	{
		Visual.Opacity = Opacity;
		Visual.MarkDirty();
	}

	// --- Independent (no-relayout) animation channels ---
	// The animation clock writes these directly and marks the visual dirty; because they are composed back into the
	// visual in SyncVisual, a later relayout cannot clobber them. RenderTransform is not a dependency property, so a
	// translate animation rides this override rather than the DP rung (Opacity, a DP, goes through SetAnimatedValue).

	private Vector2 _animatedTranslate;
	private bool _hasAnimatedTranslate;
	private HdrColor? _animatedFill;

	/// <summary>The animation-overridden brush, if any; a content-bearing control's <see cref="UpdateContent"/> prefers
	/// it over its background so a colour animation drives the brush on the no-relayout visual path.</summary>
	private protected HdrColor? AnimatedFill => _animatedFill;

	/// <summary>The colour this element paints now: the animated fill if one is running, else its own
	/// (<see cref="BaseFill"/>), else transparent. Where a colour animation with no <c>From</c> starts.</summary>
	internal HdrColor CurrentFill => _animatedFill ?? BaseFill ?? HdrColor.Transparent;

	/// <summary>The colour the animated fill overrides — a control's background, a text block's foreground — or null
	/// for an element that paints none.</summary>
	private protected virtual HdrColor? BaseFill => null;

	/// <summary>The offset an animation currently adds to the render transform; zero when none is running.</summary>
	internal Vector2 AnimatedTranslate => _animatedTranslate;

	/// <summary>The element's effective composition transform: render transform, plus any animated translate, placed
	/// at the layout position. Used by both <see cref="SyncVisual"/> and the translate animation so they agree.</summary>
	private Matrix3x2 ComposeTransform()
	{
		var t = RenderTransform;
		if (_hasAnimatedTranslate)
			t *= Matrix3x2.CreateTranslation(_animatedTranslate);
		return t * Matrix3x2.CreateTranslation(LayoutRect.X, LayoutRect.Y);
	}

	private void ApplyAnimatedTransform()
	{
		Visual.Transform = ComposeTransform();
		Visual.MarkDirty();
	}

	internal void SetAnimatedTranslateX(float x) { _animatedTranslate.X = x; _hasAnimatedTranslate = true; ApplyAnimatedTransform(); }
	internal void SetAnimatedTranslateY(float y) { _animatedTranslate.Y = y; _hasAnimatedTranslate = true; ApplyAnimatedTransform(); }

	internal void ClearAnimatedTranslate()
	{
		_hasAnimatedTranslate = false;
		_animatedTranslate = default;
		ApplyAnimatedTransform();
	}

	/// <summary>Push an animated brush onto this element's drawn content (no relayout).</summary>
	internal void SetAnimatedFill(HdrColor color) { _animatedFill = color; InvalidateContent(); }

	/// <summary>Clear the animated brush, reverting to the element's own background.</summary>
	internal void ClearAnimatedFill() { _animatedFill = null; InvalidateContent(); }

	/// <summary>Bring just this element's drawn content up to date in place (a brush/background change) against the
	/// current <see cref="LayoutRect"/> — no relayout. Marks the visual dirty for the next composite.</summary>
	protected void InvalidateContent()
	{
		Visual.Content = UpdateContent();
		Visual.MarkDirty();
	}

	protected override void OnPropertyInvalidated(DependencyProperty dp, InvalidationFlags flags)
	{
		if ((flags & InvalidationFlags.Measure) != 0)
			InvalidateMeasure();
		else if ((flags & InvalidationFlags.Arrange) != 0)
			InvalidateArrange();
		if ((flags & InvalidationFlags.Render) != 0)
			InvalidateVisual();
	}

	// --- Element -> composition sync ---

	internal void SyncVisual()
	{
		Visual.Transform = ComposeTransform();
		Visual.Opacity = Opacity;
		Visual.Clip = ClipToBounds || Clipping.GetClipToBounds(this) ? new Rect(0f, 0f, LayoutRect.Width, LayoutRect.Height) : null;
		Visual.Content = UpdateContent();
		// A collapsed element's children were never re-arranged this pass (Arrange short-circuits ArrangeCore above),
		// so their LayoutRect/transform are stale leftovers from when the element was last visible. Detaching them
		// from the visual tree here (rather than only zeroing this element's own size) keeps "Collapsed contributes
		// zero to layout" true for rendering too — otherwise stale descendants would still draw at their old position.
		SyncVisualChildren(Visibility == Visibility.Collapsed ? [] : VisualChildren);
		Visual.MarkDirty();
	}

	// Mirror the element children's visuals into this visual's child list. An arrange usually leaves the children as
	// they were, so the list is compared first and relinked only when the set or its order changed.
	private void SyncVisualChildren(IReadOnlyList<UIElement> children)
	{
		var visuals = Visual.Children;
		if (visuals.Count == children.Count)
		{
			var same = true;
			for (var i = 0; same && i < children.Count; i++)
				same = ReferenceEquals(visuals[i], children[i].Visual);
			if (same)
				return;
		}

		Visual.Clear();
		for (var i = 0; i < children.Count; i++)
			Visual.Add(children[i].Visual);
	}

	/// <summary>
	/// Bring this element's drawn content up to date with its properties and <see cref="LayoutRect"/>, and return it,
	/// or null when the element draws nothing. An element keeps its content object and updates it in place, so this
	/// allocates only the first time an element draws.
	/// </summary>
	private protected virtual VisualContent? UpdateContent() => null;

	// The rect content of an element in the rect family (a background, a border), kept across updates.
	private RectContent? _rectContent;

	/// <summary>This element's retained <see cref="RectContent"/>, updated to its layout size and the given paint.</summary>
	private protected RectContent RetainedRect(HdrColor? fill, float cornerRadius = 0f, HdrColor? borderBrush = null, float borderThickness = 0f)
	{
		var content = _rectContent ??= new RectContent();
		content.Update(new Vector2(LayoutRect.Width, LayoutRect.Height), fill, cornerRadius, borderBrush, borderThickness);
		return content;
	}

	// --- Explicit pointer capture ---

	/// <summary>Request exclusive routing of all pointer events for <paramref name="pointerId"/> to this element,
	/// regardless of where the pointer moves (drag support). Returns false when not attached to a live host.</summary>
	public bool CapturePointer(uint pointerId)
	{
		var im = AttachedRoot?.InputManager;
		return im is not null && im.RequestCapture(pointerId, this);
	}

	/// <summary>Release explicit capture for <paramref name="pointerId"/>; raises
	/// <see cref="PointerCaptureLostEvent"/> on this element.</summary>
	public void ReleasePointerCapture(uint pointerId) =>
		AttachedRoot?.InputManager?.RequestRelease(pointerId, this);

	// --- Routed-event handler store + dispatch ---

	private struct HandlerSlot
	{
		public RoutedEventHandler? Normal;     // invoked only when !args.Handled
		public RoutedEventHandler? HandledToo; // invoked regardless of Handled
	}

	private Dictionary<ushort, HandlerSlot>? _handlers;
	[ThreadStatic] private static Stack<List<UIElement>>? _routePool;

	/// <summary>Attach a typed handler for <paramref name="evt"/>. If <paramref name="handledEventsToo"/> is true
	/// the handler fires even after a previous handler has set <see cref="RoutedEventArgs.Handled"/>.</summary>
	public void AddHandler(RoutedEvent evt, RoutedEventHandler handler, bool handledEventsToo = false)
	{
		_handlers ??= [];
		_handlers.TryGetValue(evt.Id, out var slot);
		if (handledEventsToo)
			slot.HandledToo = (RoutedEventHandler?)Delegate.Combine(slot.HandledToo, handler);
		else
			slot.Normal = (RoutedEventHandler?)Delegate.Combine(slot.Normal, handler);
		_handlers[evt.Id] = slot;
	}

	/// <summary>Detach a handler previously added with <see cref="AddHandler"/>.</summary>
	public void RemoveHandler(RoutedEvent evt, RoutedEventHandler handler)
	{
		if (_handlers is null || !_handlers.TryGetValue(evt.Id, out var slot))
			return;
		slot.Normal = (RoutedEventHandler?)Delegate.Remove(slot.Normal, handler);
		slot.HandledToo = (RoutedEventHandler?)Delegate.Remove(slot.HandledToo, handler);
		if (slot.Normal is null && slot.HandledToo is null)
			_handlers.Remove(evt.Id);
		else
			_handlers[evt.Id] = slot;
	}

	/// <summary>
	/// Raise <paramref name="evt"/> on this element as the original source and route it:
	/// tunnel root→target, bubble target→root, or direct. <see cref="RoutedEventArgs.Handled"/> prevents
	/// <c>Normal</c> handlers from firing at subsequent positions, but <c>handledEventsToo</c> handlers always fire.
	/// The ancestor chain is built into a pooled, re-entrancy-safe buffer (a handler may raise another event).
	/// </summary>
	internal void RaiseEvent(RoutedEvent evt, RoutedEventArgs args)
	{
		args.Reset(evt, this);

		var pool = _routePool ??= new Stack<List<UIElement>>();
		var route = pool.Count > 0 ? pool.Pop() : new List<UIElement>(16);
		for (UIElement? e = this; e is not null; e = e.Parent)
			route.Add(e);

		switch (evt.Routing)
		{
			case RoutingStrategy.Tunnel:
				for (var i = route.Count - 1; i >= 0; i--)
					route[i].InvokeHandlers(args);
				break;
			case RoutingStrategy.Direct:
				InvokeHandlers(args);
				break;
			default: // Bubble
				for (var i = 0; i < route.Count; i++)
					route[i].InvokeHandlers(args);
				break;
		}

		route.Clear();
		pool.Push(route);
	}

	private void InvokeHandlers(RoutedEventArgs args)
	{
		if (_handlers is null || args.RoutedEvent is null || !_handlers.TryGetValue(args.RoutedEvent.Id, out var slot))
			return;
		// A disabled control handles no input on itself; the event still routes on to its ancestors.
		if (args.RoutedEvent.IsInput && this is Control { CanHandleInput: false })
			return;
		args.Source = this;
		if (!args.Handled)
			slot.Normal?.Invoke(this, args);
		slot.HandledToo?.Invoke(this, args);
	}

	// --- 2D hit-testing: reverse-z, respects IsHitTestVisible/Visibility/RenderTransform ---

	/// <summary>
	/// Return the top-most descendant (or this element) hit by <paramref name="point"/>, expressed in this element's
	/// local space (origin at this element's top-left, pre-<see cref="RenderTransform"/>). Children are tested
	/// front-to-back; an element is a hit only where it actually draws (its visual has content), so transparent
	/// panels let clicks fall through. Pure + side-effect-free — asserted directly in tests.
	/// </summary>
	internal UIElement? HitTest(Vector2 point)
	{
		if (!IsHitTestVisible || Visibility != Visibility.Visible)
			return null;

		var local = point;
		if (RenderTransform != Matrix3x2.Identity && Matrix3x2.Invert(RenderTransform, out var inv))
			local = Vector2.Transform(point, inv);

		var children = VisualChildren;
		for (var i = children.Count - 1; i >= 0; i--)
		{
			var c = children[i];
			var hit = c.HitTest(local - new Vector2(c.LayoutRect.X, c.LayoutRect.Y));
			if (hit is not null)
				return hit;
		}

		var bounds = new Rect(0f, 0f, LayoutRect.Width, LayoutRect.Height);
		return bounds.Contains(local) && Visual.Content is not null ? this : null;
	}

	internal void AdoptChild(UIElement child)
	{
		child.Parent = this;
		child.SetAttachedRoot(AttachedRoot);
		child.InheritDisabled(InDisabledControl);
	}

	internal void DropChild(UIElement child)
	{
		if (child.Parent == this)
		{
			child.Parent = null;
			child.SetAttachedRoot(null);
			child.InheritDisabled(false);
		}
	}

	// --- Control.IsEnabled inheritance ---

	/// <summary>Whether the nearest control at or above this element is disabled: such an element takes no focus, and a
	/// control adopted beneath it is disabled too.</summary>
	internal bool InDisabledControl
	{
		get
		{
			for (var e = this; e is not null; e = e.Parent)
			{
				if (e is Control control)
					return !control.IsEnabled;
			}
			return false;
		}
	}

	/// <summary>Tell the topmost controls in this subtree whether the control above them is disabled. Each passes the
	/// change on to its own subtree if its effective <see cref="Control.IsEnabled"/> moves.</summary>
	internal void InheritDisabled(bool ancestorDisabled)
	{
		if (this is Control control)
		{
			control.SetAncestorDisabled(ancestorDisabled);
			return;
		}
		foreach (var child in VisualChildren)
			child.InheritDisabled(ancestorDisabled);
	}
}
