using System.Numerics;
using SUIsei.Animation;
using SUIsei.Composition;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Extensions;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei;

/// <summary>
/// The base of every visual element: two-pass measure/arrange, layout invalidation, the
/// owned composition <see cref="Visual"/>, render-affecting DPs, routed events (pointer + keyboard + focus),
/// hit-testing, and focus/capture queries. The WinUI <c>MeasureCore</c>/<c>ArrangeCore</c> seam lets
/// <see cref="FrameworkElement"/> layer Margin/Min/Max/alignment on top of the element's intrinsic sizing.
/// <para>
/// While it is in a live tree an element listens to the brushes and the transform it shows, so a change to a shared one
/// reaches it on the visual path, with no layout; it stops listening when it leaves, so they do not keep it alive.
/// </para>
/// </summary>
public abstract partial class UIElement : DependencyObject, ISubPropertyListener
{
	// --- Dependency properties ---

	public static readonly DependencyProperty<UIElement, float> OpacityProperty =
		DependencyProperty.Register(nameof(Opacity),
			new PropertyMetadata<UIElement, float> { DefaultValue = 1f, Invalidates = InvalidationFlags.Render });

	public static readonly DependencyProperty<UIElement, Visibility> VisibilityProperty =
		DependencyProperty.Register(nameof(Visibility),
			new PropertyMetadata<UIElement, Visibility>
			{
				DefaultValue = Visibility.Visible,
				Invalidates = InvalidationFlags.Measure,
				PropertyChanged = static (e, _, visibility) => e.OnVisibilityChanged(visibility),
			});

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

	/// <summary><see cref="Visibility.Collapsed"/> contributes zero to layout, draws nothing and never
	/// hit-tests.</summary>
	public Visibility Visibility { get => GetValue(VisibilityProperty); set => SetValue(VisibilityProperty, value); }

	/// <summary>When false, this element (and its subtree, by the walk) is skipped by hit-testing.</summary>
	public bool IsHitTestVisible { get => GetValue(IsHitTestVisibleProperty); set => SetValue(IsHitTestVisibleProperty, value); }

	public bool IsTabStop { get => GetValue(IsTabStopProperty); set => SetValue(IsTabStopProperty, value); }

	/// <summary>
	/// Attempts to give the element keyboard focus, as WinUI's <c>UIElement.Focus</c>: through its root's focus manager, raising
	/// the focus events as any move does. False when it isn't attached to a root, can't take focus (not a tab stop, not
	/// visible, or in a disabled control), or a handler cancelled the move.
	/// </summary>
	/// <param name="value">How it takes focus: <see cref="FocusState.Programmatic"/> from code, or as a keyboard or a pointer
	/// would give it.</param>
	/// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="FocusState.Unfocused"/>, which is no way to
	/// take focus.</exception>
	public bool Focus(FocusState value)
	{
		if (value == FocusState.Unfocused)
			throw new ArgumentException("An element can't take focus as Unfocused.", nameof(value));
		return AttachedRoot?.FocusManager is { } focus && focus.TrySetFocus(this, value);
	}

	public int TabIndex { get => GetValue(TabIndexProperty); set => SetValue(TabIndexProperty, value); }

	/// <summary>Where focus goes from here on a d-pad's up, as WinUI's <c>XYFocusUp</c>: overrides the nearest tab stop
	/// that way (<see cref="FocusManager"/>) while it is focusable. Null for the nearest.</summary>
	public UIElement? XYFocusUp { get; set; }

	/// <summary>Where focus goes from here on a d-pad's down; null for the nearest tab stop that way.</summary>
	public UIElement? XYFocusDown { get; set; }

	/// <summary>Where focus goes from here on a d-pad's left; null for the nearest tab stop that way.</summary>
	public UIElement? XYFocusLeft { get; set; }

	/// <summary>Where focus goes from here on a d-pad's right; null for the nearest tab stop that way.</summary>
	public UIElement? XYFocusRight { get; set; }

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

	/// <summary>The transform applied to the element as it is drawn, about <see cref="RenderTransformOrigin"/>, with no
	/// effect on layout. Its properties animate through paths such as
	/// <c>(UIElement.RenderTransform).(CompositeTransform.TranslateX)</c>.</summary>
	public static readonly DependencyProperty<UIElement, Transform?> RenderTransformProperty =
		DependencyProperty.Register(nameof(RenderTransform),
			new PropertyMetadata<UIElement, Transform?> { PropertyChanged = static (e, old, @new) => e.OnRenderTransformChanged(old, @new) });

	/// <summary>The point <see cref="RenderTransform"/> is applied about, as a fraction of the element's size: (0,0) its
	/// top left, (0.5,0.5) its centre.</summary>
	public static readonly DependencyProperty<UIElement, Point> RenderTransformOriginProperty =
		DependencyProperty.Register(nameof(RenderTransformOrigin),
			new PropertyMetadata<UIElement, Point> { PropertyChanged = static (e, _, _) => e.ApplyTransform() });

	/// <summary>The transform applied to the element as it is drawn, with no effect on layout; null for none.</summary>
	public Transform? RenderTransform { get => GetValue(RenderTransformProperty); set => SetValue(RenderTransformProperty, value); }

	/// <summary>The point <see cref="RenderTransform"/> is applied about, relative to the element's size.</summary>
	public Point RenderTransformOrigin { get => GetValue(RenderTransformOriginProperty); set => SetValue(RenderTransformOriginProperty, value); }

	/// <summary>
	/// An offset in DIPs added to where the element is drawn, after its <see cref="RenderTransform"/>, as WinUI 3's
	/// <c>UIElement.Translation</c>. A plain property rather than a dependency property: setting it writes the element's
	/// composition visual directly, with no layout and no property-store cost, so it suits a value set every frame, such
	/// as a label tracking a point in a 3D scene. Hit-testing and <see cref="TransformToVisual"/> follow it. Animate it
	/// from code with <see cref="Animation.AnimationClock.Tween{TTarget, T}(TTarget, Func{TTarget, T}, Action{TTarget, T}, T, TimeSpan, Animation.IEasingFunction, Animation.Lerp{T})"/>.
	/// SUIsei composites in 2D, so <c>Z</c> is kept but moves nothing, as it moves nothing in WinUI without a perspective.
	/// </summary>
	public Vector3 Translation
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			ApplyTransform();
		}
	}

	/// <summary>
	/// Whether this element always clips its subtree to its layout bounds, as a control that needs it (ScrollViewer,
	/// TextBox) sets it. Markup asks for the same with <see cref="Clipping.ClipToBoundsProperty"/>; the clip applies
	/// when either is set, intersected with <see cref="Clip"/> (in <see cref="EffectiveClip"/>).
	/// </summary>
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
		var previous = AttachedRoot;
		AttachTree(root);
		// Once the whole subtree has left, what the root it left held for any of it is let go: focus, pointer capture,
		// over-chains.
		if (previous is not null && !ReferenceEquals(previous, root))
			previous.ReleaseWithin(this);
	}

	private void AttachTree(UIRoot? root)
	{
		var wasAttached = AttachedRoot is not null;
		AttachedRoot = root;
		if (wasAttached != root is not null)
			OnLiveTreeChanged(root is not null);
		foreach (var child in VisualChildren)
			child.AttachTree(root);
	}

	/// <summary>Whether this element is <paramref name="subtree"/> or lies beneath it.</summary>
	internal bool IsWithin(UIElement subtree)
	{
		for (var e = this; e is not null; e = e.Parent)
		{
			if (ReferenceEquals(e, subtree))
				return true;
		}
		return false;
	}

	/// <summary>
	/// The element joined (<paramref name="attached"/>) or left a live tree: start or stop listening to the brushes,
	/// transform and clip geometry it shows. An override registers its own brush properties through
	/// <see cref="Listen(Brush, bool)"/> and calls the base. Joining also re-applies the transform and the clip and marks the
	/// visual dirty, since a shared brush, transform or geometry may have changed while the element was away, and a layout
	/// pass that finds the element where it was arranges nothing.
	/// </summary>
	private protected virtual void OnLiveTreeChanged(bool attached)
	{
		Listen(RenderTransform, attached);
		Listen(Clip, attached);
		if (attached)
		{
			ApplyTransform();
			ApplyClip();
			Visual.MarkDirty();
		}
	}

	/// <summary>Start or stop listening to <paramref name="brush"/>.</summary>
	private protected void Listen(Brush? brush, bool listen)
	{
		if (brush is null)
			return;
		if (listen)
			brush.AddListener(this);
		else
			brush.RemoveListener(this);
	}

	private void Listen(Transform? transform, bool listen)
	{
		if (transform is null)
			return;
		if (listen)
			transform.AddListener(this);
		else
			transform.RemoveListener(this);
	}

	/// <summary>A brush property changed from <paramref name="old"/> to <paramref name="new"/>: move the listener
	/// registration if the element is in a live tree, and repaint its content in place, with no layout.</summary>
	private protected void OnBrushChanged(Brush? old, Brush? @new)
	{
		if (AttachedRoot is not null)
		{
			Listen(old, false);
			Listen(@new, true);
		}
		InvalidateContent();
	}

	private void OnRenderTransformChanged(Transform? old, Transform? @new)
	{
		if (AttachedRoot is not null)
		{
			Listen(old, false);
			Listen(@new, true);
		}
		ApplyTransform();
	}

	void ISubPropertyListener.OnSubPropertyChanged(object source) => OnSubPropertyChanged(source);

	/// <summary>
	/// Something the element listens to changed in place: a brush, its render transform, its clip geometry, or what an
	/// override listens to itself, such as a shape's geometry. An override handles its own sources and passes the rest
	/// to the base.
	/// </summary>
	private protected virtual void OnSubPropertyChanged(object source)
	{
		// A transform moved: recompose the visual's transform. The clip geometry moved: re-clip, with no layout. A brush
		// changed its paint in place: the content already holds that paint, so the visual only needs recompositing.
		if (source is Transform)
			ApplyTransform();
		else if (ReferenceEquals(source, Clip))
			ApplyClip();
		else
			Visual.MarkDirty();
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
		finalRect = Extensions.SafeArea.Inset(this, Extensions.InputPane.Avoid(this, finalRect));
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

	/// <summary>Invalidate the measure of this element and of everything under it, for a change every element's layout
	/// may read, such as the root's font.</summary>
	internal void InvalidateMeasureTree()
	{
		InvalidateMeasure();
		var children = VisualChildren;
		for (var i = 0; i < children.Count; i++)
			children[i].InvalidateMeasureTree();
	}

	/// <summary>Invalidate the arrange of this element and of everything under it, for a change any element's placement
	/// may read, such as the root's on-screen keyboard.</summary>
	internal void InvalidateArrangeTree()
	{
		InvalidateArrange();
		var children = VisualChildren;
		for (var i = 0; i < children.Count; i++)
			children[i].InvalidateArrangeTree();
	}

	/// <summary>The font this element's text measures and draws with: its root's <see cref="UIRoot.Font"/>, or
	/// <see cref="FontAtlas.Default"/> while it is in no root.</summary>
	private protected FontAtlas TextFont => AttachedRoot?.Font ?? FontAtlas.Default;

	/// <summary>A render-only change (opacity, brush): refresh the composition node's render state and mark it dirty
	/// for the next composite — no relayout. Layout-driven sync happens in <see cref="SyncVisual"/> after arrange.</summary>
	public void InvalidateVisual()
	{
		Visual.Opacity = Opacity;
		Visual.MarkDirty();
	}

	// --- The visual's transform ---

	/// <summary>The element's composition transform: its render transform about its origin, then its translation, placed
	/// at its layout position. Layout (<see cref="SyncVisual"/>), a transform change and <see cref="Translation"/> all
	/// compose it here, so they agree.</summary>
	private Matrix3x2 ComposeTransform()
	{
		var t = Matrix3x2.Identity;
		if (RenderTransform is { } render)
		{
			var origin = RenderTransformOrigin;
			var pivot = new Vector2(origin.X * LayoutRect.Width, origin.Y * LayoutRect.Height);
			t = Matrix3x2.CreateTranslation(-pivot) * render.ToMatrix() * Matrix3x2.CreateTranslation(pivot);
		}
		return t * Matrix3x2.CreateTranslation(Translation.X + LayoutRect.X, Translation.Y + LayoutRect.Y);
	}

	/// <summary>Write the composed transform to the visual (which marks it dirty if it moved), with no layout.</summary>
	private void ApplyTransform() => Visual.Transform = ComposeTransform();

	// --- Background transitions ---

	// A running BrushTransition: the colour the background moves from and to, and the paint it shows meanwhile.
	private sealed class RunningTransition
	{
		public readonly CompositionColorBrush Paint = new();
		public HdrColor From;
		public HdrColor To;
		public bool Running;
	}

	private RunningTransition? _backgroundTransition;

	/// <summary>The paint an element's background shows for <paramref name="background"/>: its brush's own, or the
	/// transition's while a <see cref="BrushTransition"/> is running.</summary>
	private protected CompositionBrush? BackgroundPaint(Brush? background) =>
		_backgroundTransition is { Running: true } transition ? transition.Paint : background?.CompositionBrush;

	/// <summary>
	/// The background changed from <paramref name="old"/> to <paramref name="new"/>: with a <paramref name="transition"/>,
	/// the element on a live host's clock, and solid colours on both sides (or a solid colour and no brush, which fades
	/// that colour in or out), animate from the colour on screen to the new one. Otherwise end any transition, so the new
	/// brush shows at once. Call before updating the content.
	/// </summary>
	private protected void TransitionBackground(BrushTransition? transition, Brush? old, Brush? @new)
	{
		if (transition is null || AttachedRoot?.Clock is not { } clock || !TryFade(old, @new, out var start, out var end))
		{
			if (_backgroundTransition is { } running)
				running.Running = false;
			return;
		}

		var state = _backgroundTransition ??= new RunningTransition();
		state.From = state.Running ? state.Paint.Color : start;
		state.To = end;
		state.Paint.Color = state.From;
		state.Running = true;
		clock.Tween(this, static _ => 0f, static (e, progress) => e.StepBackgroundTransition(progress), 1f, transition.Duration);

		// The colours a fade runs between: each solid brush's colour with its opacity folded into the alpha, and for a
		// missing brush the other's colour made transparent. False for anything else.
		static bool TryFade(Brush? old, Brush? @new, out HdrColor start, out HdrColor end)
		{
			start = end = default;
			if (old is not (null or SolidColorBrush) || @new is not (null or SolidColorBrush) || (old is null && @new is null))
				return false;
			start = old is SolidColorBrush o ? Shown(o) : Shown((SolidColorBrush)@new!) with { A = 0f };
			end = @new is SolidColorBrush n ? Shown(n) : start with { A = 0f };
			return true;
		}

		static HdrColor Shown(SolidColorBrush brush) => brush.Color with { A = brush.Color.A * brush.Opacity };
	}

	private void StepBackgroundTransition(float progress)
	{
		if (_backgroundTransition is not { Running: true } state)
			return;
		if (progress < 1f)
		{
			state.Paint.Color = Lerps.Color(state.From, state.To, progress);
			Visual.MarkDirty();
			return;
		}
		state.Running = false;
		InvalidateContent(); // hand the background back to the brush itself
	}

	/// <summary>Bring just this element's drawn content up to date in place (a brush/background change) against the
	/// current <see cref="LayoutRect"/> — no relayout; a collapsed element stays drawing nothing. Marks the visual dirty
	/// for the next composite.</summary>
	protected void InvalidateContent()
	{
		Visual.Content = ShownContent();
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
		Visual.Clip = EffectiveClip();
		Visual.Content = ShownContent();
		// A collapsed element's children were never re-arranged this pass (Arrange short-circuits ArrangeCore above),
		// so their LayoutRect/transform are stale leftovers from when the element was last visible. Detaching them
		// from the visual tree here (rather than only zeroing this element's own size) keeps "Collapsed contributes
		// zero to layout" true for rendering too — otherwise stale descendants would still draw at their old position.
		SyncVisualChildren(Visibility == Visibility.Collapsed ? [] : VisualChildren);
		Visual.MarkDirty();
	}

	/// <summary>
	/// What the element's visual draws: its content brought up to date, or nothing while it is collapsed. A collapsed
	/// element is not arranged, so its content would be drawn from a stale or empty layout at the place it was given:
	/// text and a polyline would draw in full there. It keeps its content object, which its next arrange once it is shown
	/// brings up to date, so showing it again allocates nothing.
	/// </summary>
	private VisualContent? ShownContent() => Visibility == Visibility.Collapsed ? null : UpdateContent();

	/// <summary>
	/// Collapsed: stop drawing now, the element's content and its children, rather than at its next arrange, which a
	/// panel need not give a collapsed child. Shown again, it draws once layout has placed it.
	/// </summary>
	private void OnVisibilityChanged(Visibility visibility)
	{
		if (visibility != Visibility.Collapsed)
			return;
		Visual.Content = null;
		SyncVisualChildren([]);
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
	private protected RectContent RetainedRect(CompositionBrush? fill, CompositionCornerRadius cornerRadius = default, CompositionBrush? borderBrush = null,
		float borderThickness = 0f)
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

	/// <summary>
	/// Return the top-most element of this subtree drawn at <paramref name="surfacePoint"/> (surface pixels). The walk
	/// follows the composition exactly as the compositor draws it: each visual's <see cref="SUIsei.Composition.Visual.Transform"/>
	/// is composed onto <paramref name="parentWorld"/>, so render transforms, animated translates and layout scale all
	/// count, and each visual's clip is intersected into <paramref name="clip"/> as its surface-space bounds, the same
	/// bound the compositor clips with, so a point is hittable exactly where something can be seen. Children are tested
	/// front-to-back before the element's own content, and an element is a hit only where its content draws
	/// (<see cref="ContentContains"/>), so transparent panels let the point through. With
	/// <paramref name="includeHidden"/>, elements whose <see cref="IsHitTestVisible"/> is false are tested too, as a
	/// diagnostics pick does. Allocation-free.
	/// </summary>
	internal UIElement? HitTest(Vector2 surfacePoint, in Matrix3x2 parentWorld, Rect clip, bool includeHidden = false)
	{
		var skipped = !includeHidden && !IsHitTestVisible;
		if (skipped || Visibility != Visibility.Visible)
			return null;

		var world = Visual.Transform * parentWorld;
		if (Visual.Clip is { } localClip)
			clip = clip.Intersect(localClip.TransformBounds(world));
		if (!clip.Contains(surfacePoint))
			return null;

		var children = VisualChildren;
		for (var i = children.Count - 1; i >= 0; i--)
		{
			if (children[i].HitTest(surfacePoint, world, clip, includeHidden) is { } hit)
				return hit;
		}

		return Visual.Content is { } content && Matrix3x2.Invert(world, out var toLocal)
			&& ContentContains(content, Vector2.Transform(surfacePoint, toLocal), LayoutRect, world)
			? this
			: null;
	}

	/// <summary>
	/// Whether <paramref name="content"/>, drawn in an element's local space, covers <paramref name="local"/>: one case
	/// per content kind, as the compositor draws one per kind. A rect covers its bounds, except that one filled only by an
	/// image covers just the image (not the letterbox around it), and text covers its element's layout bounds. A shape
	/// covers what it draws (<see cref="ShapeContent.HitTest"/>), tessellated for <paramref name="world"/>'s scale as the
	/// compositor draws it, so the test reuses the drawn tessellation.
	/// </summary>
	private static bool ContentContains(VisualContent content, Vector2 local, in Rect layoutRect, in Matrix3x2 world) => content switch
	{
		RectContent { Fill: CompositionSurfaceBrush image, BorderBrush: null } rect => image.PaintedRect(rect.Size).Contains(local),
		RectContent rect => new Rect(0f, 0f, rect.Size.X, rect.Size.Y).Contains(local),
		ShapeContent shape => shape.HitTest(local, TessellationScale.For(world)),
		_ => new Rect(0f, 0f, layoutRect.Width, layoutRect.Height).Contains(local),
	};

	/// <summary>The transform from this element's local space to its root's DIP space: the visual transforms composed up
	/// the parent chain, as drawn. With the root, also the top element it reached, which identifies the tree.</summary>
	internal Matrix3x2 TransformToRoot(out UIElement top)
	{
		var m = Matrix3x2.Identity;
		top = this;
		for (UIElement? e = this; e is not null; e = e.Parent)
		{
			m *= e.Visual.Transform;
			top = e;
		}
		return m;
	}

	/// <summary>
	/// A transform from this element's local space to <paramref name="visual"/>'s, or to the root's DIP space when it is
	/// null, as WinUI's <c>UIElement.TransformToVisual</c>. It goes through the composed visual transforms, so render
	/// transforms, <see cref="Translation"/> and scroll offsets count. A snapshot: it does not follow either element when
	/// it moves. The transform it returns is a <see cref="MatrixTransform"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="visual"/> is not in the same tree as this element.</exception>
	public GeneralTransform TransformToVisual(UIElement? visual)
	{
		var toRoot = TransformToRoot(out var top);
		if (visual is null)
			return new MatrixTransform { Matrix = toRoot };

		var otherToRoot = visual.TransformToRoot(out var otherTop);
		var sameTree = ReferenceEquals(top, otherTop) || (AttachedRoot is not null && ReferenceEquals(AttachedRoot, visual.AttachedRoot));
		if (!sameTree)
			throw new ArgumentException("The element is not in the same tree as this one.", nameof(visual));
		return new MatrixTransform
		{
			Matrix = Matrix3x2.Invert(otherToRoot, out var rootToOther)
				? toRoot * rootToOther
				: new Matrix3x2(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN),
		};
	}

	internal void AdoptChild(UIElement child)
	{
		child.Parent = this;
		child.SetAttachedRoot(AttachedRoot);
		child.InheritDisabled(InDisabledControl);
		InheritedText.Adopt(this, child);
	}

	internal void DropChild(UIElement child)
	{
		if (child.Parent == this)
		{
			child.Parent = null;
			child.SetAttachedRoot(null);
			child.InheritDisabled(false);
			InheritedText.Drop(child);
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
