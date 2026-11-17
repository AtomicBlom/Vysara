using SUIsei.Extensions;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A clipping viewport over a single oversized <see cref="Content"/>, as WinUI's. Along each axis whose scroll bar visibility
/// isn't <see cref="ScrollBarVisibility.Disabled"/> (vertical by default, as WinUI's) it measures the content unbounded to
/// learn its extent; it arranges the content offset by the scroll position and clips to the viewport. Scrolls by mouse
/// wheel, by a thumb, by keyboard (Page/Home/End/arrows when a descendant is focused), by a finger where the axis's
/// <see cref="ScrollMode"/> lets it, and programmatically; the offset is always clamped to
/// <see cref="ScrollableHeight"/>/<see cref="ScrollableWidth"/>. A wheel it can't scroll any further is left unhandled, so an
/// enclosing viewer scrolls instead. Pairs with <see cref="VirtualizingStackPanel"/>, which reports its full extent for
/// exactly this viewport.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed class ScrollViewer : FrameworkElement
{
	// The thumb brightens and widens through rest → hover → grabbed, so it visibly reacts to the pointer.
	// Shared by every scroll viewer and never changed.
	private static readonly SolidColorBrush ThumbRest = Brush.Sealed(HdrColor.FromRgb(86, 92, 108));
	private static readonly SolidColorBrush ThumbHover = Brush.Sealed(HdrColor.FromRgb(124, 132, 152));
	private static readonly SolidColorBrush ThumbActive = Brush.Sealed(HdrColor.FromRgb(160, 168, 190));
	private const float ThumbThickness = 8f;
	private const float ThumbThicknessActive = 11f;
	private const float ThumbMinLength = 28f;
	private const float ThumbMargin = 2f;

	// The thumbs fade between their brushes through their BrushTransition when on a live host, and switch at once when not.
	// The vertical one runs down the right; the horizontal one along the bottom, short of the vertical one's corner.
	private readonly Border _vThumb = new()
	{
		Background = ThumbRest,
		CornerRadius = new CornerRadius(ThumbThickness / 2f),
		BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) },
	};
	private readonly Border _hThumb = new()
	{
		Background = ThumbRest,
		CornerRadius = new CornerRadius(ThumbThickness / 2f),
		BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) },
	};
	// VisualChildren (the content, then the thumbs), rebuilt only when Content changes, so reading it allocates nothing.
	private UIElement[] _visualChildren;
	private float _thumbHeight;
	private float _thumbTravel;
	private bool _thumbOver;
	private bool _draggingThumb;
	private float _dragStartPointerY;
	private float _dragStartOffset;
	private float _hThumbWidth;
	private float _hThumbTravel;
	private bool _hThumbOver;
	private bool _draggingHThumb;
	private float _dragStartPointerX;
	private float _dragStartHOffset;
	// The descendant holding keyboard focus, kept in view: scrolled to when it takes focus and again whenever the viewport
	// shrinks under it (an on-screen keyboard opening over the page).
	private UIElement? _focused;
	private bool _bringFocusedIntoView;
	// Touch panning: a finger down on the content, and whether it has travelled far enough (PanSlop, in DIPs) to be a pan
	// rather than a tap; once it has, the viewer holds the pointer and the content follows the finger along each axis its
	// ScrollMode lets it.
	private const float PanSlop = 8f;
	private bool _panPressed;
	private bool _panning;
	private uint _panPointer;
	private float _panStartX;
	private float _panStartY;
	private float _panStartHOffset;
	private float _panStartOffset;

	/// <summary>Identifies <see cref="HorizontalScrollBarVisibility"/>.</summary>
	public static readonly DependencyProperty<ScrollViewer, ScrollBarVisibility> HorizontalScrollBarVisibilityProperty =
		DependencyProperty.Register<ScrollViewer, ScrollBarVisibility>(nameof(HorizontalScrollBarVisibility),
			new PropertyMetadata<ScrollViewer, ScrollBarVisibility> { DefaultValue = ScrollBarVisibility.Disabled, Invalidates = InvalidationFlags.Measure });

	/// <summary>Identifies <see cref="VerticalScrollBarVisibility"/>.</summary>
	public static readonly DependencyProperty<ScrollViewer, ScrollBarVisibility> VerticalScrollBarVisibilityProperty =
		DependencyProperty.Register<ScrollViewer, ScrollBarVisibility>(nameof(VerticalScrollBarVisibility),
			new PropertyMetadata<ScrollViewer, ScrollBarVisibility> { DefaultValue = ScrollBarVisibility.Visible, Invalidates = InvalidationFlags.Measure });

	/// <summary>Identifies <see cref="HorizontalScrollMode"/>.</summary>
	public static readonly DependencyProperty<ScrollViewer, ScrollMode> HorizontalScrollModeProperty =
		DependencyProperty.Register<ScrollViewer, ScrollMode>(nameof(HorizontalScrollMode),
			new PropertyMetadata<ScrollViewer, ScrollMode> { DefaultValue = ScrollMode.Enabled });

	/// <summary>Identifies <see cref="VerticalScrollMode"/>.</summary>
	public static readonly DependencyProperty<ScrollViewer, ScrollMode> VerticalScrollModeProperty =
		DependencyProperty.Register<ScrollViewer, ScrollMode>(nameof(VerticalScrollMode),
			new PropertyMetadata<ScrollViewer, ScrollMode> { DefaultValue = ScrollMode.Enabled });

	/// <summary>
	/// Whether it scrolls sideways, and shows a thumb along the bottom when it can, as WinUI's. Disabled by default: the content
	/// takes the viewport's width.
	/// </summary>
	public ScrollBarVisibility HorizontalScrollBarVisibility
	{
		get => GetValue(HorizontalScrollBarVisibilityProperty);
		set => SetValue(HorizontalScrollBarVisibilityProperty, value);
	}

	/// <summary>
	/// Whether it scrolls up and down, and shows a thumb down the right when it can, as WinUI's. Visible by default; Disabled
	/// measures the content to the viewport's height.
	/// </summary>
	public ScrollBarVisibility VerticalScrollBarVisibility
	{
		get => GetValue(VerticalScrollBarVisibilityProperty);
		set => SetValue(VerticalScrollBarVisibilityProperty, value);
	}

	/// <summary>Whether a finger panning the content scrolls it sideways, as WinUI's. Enabled by default.</summary>
	public ScrollMode HorizontalScrollMode
	{
		get => GetValue(HorizontalScrollModeProperty);
		set => SetValue(HorizontalScrollModeProperty, value);
	}

	/// <summary>Whether a finger panning the content scrolls it up and down, as WinUI's. Enabled by default.</summary>
	public ScrollMode VerticalScrollMode
	{
		get => GetValue(VerticalScrollModeProperty);
		set => SetValue(VerticalScrollModeProperty, value);
	}

	public ScrollViewer()
	{
		ClipToBounds = true;
		_visualChildren = [_vThumb, _hThumb];
		AdoptChild(_vThumb);
		AdoptChild(_hThumb);
		AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged);
		AddHandler(KeyDownEvent, OnKeyDown);
		AddHandler(GotFocusEvent, OnDescendantGotFocus);
		AddHandler(LostFocusEvent, OnDescendantLostFocus);
		// The previews tunnel through the viewer before they reach the content, so a drag becomes a pan before the control
		// under the finger takes it as its own.
		AddHandler(PreviewPointerPressedEvent, OnPanPressed, handledEventsToo: true);
		AddHandler(PreviewPointerMovedEvent, OnPanMoved, handledEventsToo: true);
		AddHandler(PreviewPointerReleasedEvent, OnPanReleased, handledEventsToo: true);
		AddHandler(PointerCaptureLostEvent, OnPanCaptureLost);

		// Each thumb is a grab handle: hovering/grabbing it lights it up, a press starts a drag (capturing the pointer
		// so it tracks past the thumb's edges), and the drag maps pointer travel onto the scroll range.
		_vThumb.AddHandler(PointerEnteredEvent, OnThumbEntered);
		_vThumb.AddHandler(PointerExitedEvent, OnThumbExited);
		_vThumb.AddHandler(PointerPressedEvent, OnThumbPressed);
		_vThumb.AddHandler(PointerMovedEvent, OnThumbMoved);
		_vThumb.AddHandler(PointerReleasedEvent, OnThumbReleased);
		_vThumb.AddHandler(PointerCaptureLostEvent, OnThumbCaptureLost);

		_hThumb.AddHandler(PointerEnteredEvent, OnHThumbEntered);
		_hThumb.AddHandler(PointerExitedEvent, OnHThumbExited);
		_hThumb.AddHandler(PointerPressedEvent, OnHThumbPressed);
		_hThumb.AddHandler(PointerMovedEvent, OnHThumbMoved);
		_hThumb.AddHandler(PointerReleasedEvent, OnHThumbReleased);
		_hThumb.AddHandler(PointerCaptureLostEvent, OnHThumbCaptureLost);
	}

	public UIElement? Content
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			if (field is not null)
				DropChild(field);
			field = value;
			_visualChildren = value is null ? [_vThumb, _hThumb] : [value, _vThumb, _hThumb];
			if (value is not null)
				AdoptChild(value);
			InvalidateMeasure();
		}
	}

	public float VerticalOffset { get; private set; }
	public float HorizontalOffset { get; private set; }
	public float ExtentHeight { get; private set; }
	public float ExtentWidth { get; private set; }
	public float ViewportHeight { get; private set; }
	public float ViewportWidth { get; private set; }

	/// <summary>How far it can scroll down: none when <see cref="VerticalScrollBarVisibility"/> is Disabled.</summary>
	public float ScrollableHeight => VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0f : MathF.Max(0f, ExtentHeight - ViewportHeight);

	/// <summary>How far it can scroll sideways: none when <see cref="HorizontalScrollBarVisibility"/> is Disabled.</summary>
	public float ScrollableWidth => HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0f : MathF.Max(0f, ExtentWidth - ViewportWidth);

	/// <summary>The thumb's current interaction-state target colour (test seam): rest, hover, or grabbed.</summary>
	internal HdrColor ThumbStateColor => ((SolidColorBrush)_vThumb.Background!).Color;

	/// <summary>Where the horizontal thumb is arranged (test seam): empty while it is hidden.</summary>
	internal Rect HorizontalThumbRect => _hThumb.Visibility == Visibility.Visible ? _hThumb.LayoutRect : default;

	public void ScrollToVerticalOffset(float offset) => SetVerticalOffset(offset);

	public void ScrollToHorizontalOffset(float offset) => SetHorizontalOffset(offset);

	private static bool ShowsThumb(ScrollBarVisibility visibility) => visibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible;

	private bool PansHorizontally => HorizontalScrollMode != ScrollMode.Disabled && ScrollableWidth > 0f;

	private bool PansVertically => VerticalScrollMode != ScrollMode.Disabled && ScrollableHeight > 0f;

	private void SetVerticalOffset(float offset)
	{
		var clamped = Math.Clamp(offset, 0f, ScrollableHeight);
		if (clamped == VerticalOffset)
			return;
		VerticalOffset = clamped;
		InvalidateArrange();
	}

	private void SetHorizontalOffset(float offset)
	{
		var clamped = Math.Clamp(offset, 0f, ScrollableWidth);
		if (clamped == HorizontalOffset)
			return;
		HorizontalOffset = clamped;
		InvalidateArrange();
	}

	internal override IReadOnlyList<UIElement> VisualChildren => _visualChildren;

	protected override Size MeasureOverride(Size available)
	{
		_vThumb.Measure(available);
		_hThumb.Measure(available);
		if (Content is null)
		{
			ExtentWidth = ExtentHeight = 0f;
			return Size.Zero;
		}

		// Measure the content unbounded along each axis it scrolls, so its full extent is known regardless of the viewport.
		Content.Measure(new Size(
			HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ? available.Width : float.PositiveInfinity,
			VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? available.Height : float.PositiveInfinity));
		ExtentWidth = Content.DesiredSize.Width;
		ExtentHeight = Content.DesiredSize.Height;

		// As WinUI's, it asks for its content's size up to the space there is: a viewer in a star cell is still arranged
		// to fill it, while one sized to its content (a flyout's, an Auto row's) stays only as big as what it holds.
		return new Size(MathF.Min(ExtentWidth, available.Width), MathF.Min(ExtentHeight, available.Height));
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var shrank = finalSize.Height < ViewportHeight;
		ViewportWidth = finalSize.Width;
		ViewportHeight = finalSize.Height;
		VerticalOffset = Math.Clamp(VerticalOffset, 0f, ScrollableHeight);
		HorizontalOffset = Math.Clamp(HorizontalOffset, 0f, ScrollableWidth);

		if (Content is { } content)
		{
			var contentWidth = MathF.Max(ExtentWidth, finalSize.Width);
			var contentHeight = MathF.Max(ExtentHeight, finalSize.Height);
			content.Arrange(new Rect(-HorizontalOffset, -VerticalOffset, contentWidth, contentHeight));

			// Keep the focused descendant in view as it takes focus and as the viewport shrinks under it: its place is only
			// known once the content is arranged, so scroll then and arrange the content again at the new offsets.
			if (_focused is not null && (_bringFocusedIntoView || shrank))
			{
				_bringFocusedIntoView = false;
				if (OffsetsShowing(_focused) is { } offsets && (offsets.X != HorizontalOffset || offsets.Y != VerticalOffset))
				{
					HorizontalOffset = offsets.X;
					VerticalOffset = offsets.Y;
					content.Arrange(new Rect(-HorizontalOffset, -VerticalOffset, contentWidth, contentHeight));
				}
			}
		}

		ArrangeThumb(finalSize);
		return finalSize;
	}

	// Size each thumb to the visible fraction of the content and slide it in proportion to the scroll position; hide it when
	// there is nothing to scroll that way or its scroll bar visibility hides it. The horizontal one runs along the bottom,
	// short of the vertical one's corner.
	private void ArrangeThumb(Size finalSize)
	{
		var vertical = ShowsThumb(VerticalScrollBarVisibility) && ScrollableHeight > 0f && ViewportHeight > 0f;
		if (vertical)
		{
			_thumbHeight = Math.Clamp(ViewportHeight * (ViewportHeight / ExtentHeight), ThumbMinLength, ViewportHeight);
			_thumbTravel = ViewportHeight - _thumbHeight;
			var y = _thumbTravel > 0f ? VerticalOffset / ScrollableHeight * _thumbTravel : 0f;
			var thickness = _thumbOver || _draggingThumb ? ThumbThicknessActive : ThumbThickness; // widen while engaged
			_vThumb.Visibility = Visibility.Visible;
			_vThumb.Arrange(new Rect(finalSize.Width - thickness - ThumbMargin, y, thickness, _thumbHeight));
		}
		else
		{
			_thumbTravel = 0f;
			_vThumb.Visibility = Visibility.Collapsed;
			_vThumb.Arrange(new Rect(finalSize.Width, 0f, 0f, 0f));
		}

		if (ShowsThumb(HorizontalScrollBarVisibility) && ScrollableWidth > 0f && ViewportWidth > 0f)
		{
			var track = ViewportWidth - (vertical ? ThumbThicknessActive + ThumbMargin : 0f);
			_hThumbWidth = Math.Clamp(track * (ViewportWidth / ExtentWidth), ThumbMinLength, track);
			_hThumbTravel = track - _hThumbWidth;
			var x = _hThumbTravel > 0f ? HorizontalOffset / ScrollableWidth * _hThumbTravel : 0f;
			var thickness = _hThumbOver || _draggingHThumb ? ThumbThicknessActive : ThumbThickness;
			_hThumb.Visibility = Visibility.Visible;
			_hThumb.Arrange(new Rect(x, finalSize.Height - thickness - ThumbMargin, _hThumbWidth, thickness));
		}
		else
		{
			_hThumbTravel = 0f;
			_hThumb.Visibility = Visibility.Collapsed;
			_hThumb.Arrange(new Rect(0f, finalSize.Height, 0f, 0f));
		}
	}

	// Brighten/dim the thumbs toward their current interaction state. Grabbed wins over hover wins over rest; faded by each
	// thumb's BrushTransition when on a live host, switched at once when not (so the state is observable headlessly).
	private void UpdateThumbVisual()
	{
		_vThumb.Background = _draggingThumb ? ThumbActive : _thumbOver ? ThumbHover : ThumbRest;
		_hThumb.Background = _draggingHThumb ? ThumbActive : _hThumbOver ? ThumbHover : ThumbRest;
		InvalidateArrange(); // the engaged thumb is wider
	}

	private void OnThumbEntered(object sender, RoutedEventArgs e)
	{
		_thumbOver = true;
		UpdateThumbVisual();
	}

	private void OnThumbExited(object sender, RoutedEventArgs e)
	{
		_thumbOver = false;
		UpdateThumbVisual();
	}

	private void OnThumbPressed(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs p || ScrollableHeight <= 0f)
			return;
		_draggingThumb = true;
		_dragStartPointerY = p.GetCurrentPoint(this).Position.Y;
		_dragStartOffset = VerticalOffset;
		_vThumb.CapturePointer(0);
		UpdateThumbVisual();
		e.Handled = true;
	}

	private void OnThumbMoved(object sender, RoutedEventArgs e)
	{
		if (!_draggingThumb || e is not PointerRoutedEventArgs p)
			return;
		var dy = p.GetCurrentPoint(this).Position.Y - _dragStartPointerY;
		if (_thumbTravel > 0f)
			SetVerticalOffset(_dragStartOffset + dy * ScrollableHeight / _thumbTravel);
		e.Handled = true;
	}

	private void OnThumbReleased(object sender, RoutedEventArgs e)
	{
		if (!_draggingThumb)
			return;
		_draggingThumb = false;
		_vThumb.ReleasePointerCapture(0);
		UpdateThumbVisual();
		e.Handled = true;
	}

	private void OnThumbCaptureLost(object sender, RoutedEventArgs e)
	{
		_draggingThumb = false;
		UpdateThumbVisual();
	}

	private void OnHThumbEntered(object sender, RoutedEventArgs e)
	{
		_hThumbOver = true;
		UpdateThumbVisual();
	}

	private void OnHThumbExited(object sender, RoutedEventArgs e)
	{
		_hThumbOver = false;
		UpdateThumbVisual();
	}

	private void OnHThumbPressed(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs p || ScrollableWidth <= 0f)
			return;
		_draggingHThumb = true;
		_dragStartPointerX = p.GetCurrentPoint(this).Position.X;
		_dragStartHOffset = HorizontalOffset;
		_hThumb.CapturePointer(0);
		UpdateThumbVisual();
		e.Handled = true;
	}

	private void OnHThumbMoved(object sender, RoutedEventArgs e)
	{
		if (!_draggingHThumb || e is not PointerRoutedEventArgs p)
			return;
		var dx = p.GetCurrentPoint(this).Position.X - _dragStartPointerX;
		if (_hThumbTravel > 0f)
			SetHorizontalOffset(_dragStartHOffset + dx * ScrollableWidth / _hThumbTravel);
		e.Handled = true;
	}

	private void OnHThumbReleased(object sender, RoutedEventArgs e)
	{
		if (!_draggingHThumb)
			return;
		_draggingHThumb = false;
		_hThumb.ReleasePointerCapture(0);
		UpdateThumbVisual();
		e.Handled = true;
	}

	private void OnHThumbCaptureLost(object sender, RoutedEventArgs e)
	{
		_draggingHThumb = false;
		UpdateThumbVisual();
	}

	// A sideways wheel (a trackpad's) scrolls across; an upright one scrolls down, or across when there is nothing to scroll
	// down. Handled only when it moved something: a wheel at the end of the range goes on to an enclosing viewer, as WinUI
	// chains it.
	private void OnPointerWheelChanged(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs pointer)
			return;
		var amount = Scrolling.GetWheelScrollAmount(this);
		var (horizontal, vertical) = (HorizontalOffset, VerticalOffset);
		if (pointer.Wheel.X != 0f)
			SetHorizontalOffset(HorizontalOffset - pointer.Wheel.X * amount);
		if (pointer.Wheel.Y != 0f)
		{
			// Wheel up (positive delta) scrolls toward the top (offset decreases).
			if (ScrollableHeight > 0f)
				SetVerticalOffset(VerticalOffset - pointer.Wheel.Y * amount);
			else
				SetHorizontalOffset(HorizontalOffset - pointer.Wheel.Y * amount);
		}
		if (HorizontalOffset != horizontal || VerticalOffset != vertical)
			e.Handled = true;
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key)
			return;
		var step = Scrolling.GetWheelScrollAmount(this);
		switch (key.Key)
		{
			case VirtualKey.PageDown: SetVerticalOffset(VerticalOffset + ViewportHeight); e.Handled = true; break;
			case VirtualKey.PageUp: SetVerticalOffset(VerticalOffset - ViewportHeight); e.Handled = true; break;
			case VirtualKey.Home: SetVerticalOffset(0f); e.Handled = true; break;
			case VirtualKey.End: SetVerticalOffset(ScrollableHeight); e.Handled = true; break;
			case VirtualKey.Down: SetVerticalOffset(VerticalOffset + step); e.Handled = true; break;
			case VirtualKey.Up: SetVerticalOffset(VerticalOffset - step); e.Handled = true; break;
			// Left and Right are left alone where there is nothing to scroll sideways, so they still move focus.
			case VirtualKey.Right when ScrollableWidth > 0f: SetHorizontalOffset(HorizontalOffset + step); e.Handled = true; break;
			case VirtualKey.Left when ScrollableWidth > 0f: SetHorizontalOffset(HorizontalOffset - step); e.Handled = true; break;
		}
	}

	private void OnDescendantGotFocus(object sender, RoutedEventArgs e)
	{
		if (e.OriginalSource is not UIElement element || ReferenceEquals(element, this))
			return;
		_focused = element;
		_bringFocusedIntoView = true;
		InvalidateArrange();
	}

	private void OnDescendantLostFocus(object sender, RoutedEventArgs e)
	{
		if (ReferenceEquals(e.OriginalSource, _focused))
			_focused = null;
	}

	// A finger down on the content may be the start of a pan, where an axis's ScrollMode lets one scroll it. A mouse scrolls
	// with its wheel and the thumbs instead, and a finger on a thumb drags the thumb.
	private void OnPanPressed(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs p || p.Device != PointerDeviceType.Touch || !(PansHorizontally || PansVertically)
			|| ReferenceEquals(e.OriginalSource, _vThumb) || ReferenceEquals(e.OriginalSource, _hThumb))
			return;
		_panPressed = true;
		_panning = false;
		_panPointer = p.PointerId;
		var position = p.GetCurrentPoint(this).Position;
		_panStartX = position.X;
		_panStartY = position.Y;
		_panStartHOffset = HorizontalOffset;
		_panStartOffset = VerticalOffset;
	}

	// Past the slop, along the axes it may pan, the press becomes a pan: the viewer takes the pointer, which ends the press on
	// the control under the finger without a click, and from then on the content follows the finger.
	private void OnPanMoved(object sender, RoutedEventArgs e)
	{
		if (!_panPressed || e is not PointerRoutedEventArgs p || p.PointerId != _panPointer)
			return;
		var position = p.GetCurrentPoint(this).Position;
		var (horizontally, vertically) = (PansHorizontally, PansVertically);
		if (!_panning)
		{
			var dx = horizontally ? position.X - _panStartX : 0f;
			var dy = vertically ? position.Y - _panStartY : 0f;
			var travel = MathF.Sqrt(dx * dx + dy * dy);
			if (travel < PanSlop)
				return;
			_panning = true;
			// Follow the finger from the slop's edge, so the content neither jumps by the slop nor loses travel past it that a
			// coalesced move already carries.
			_panStartX += dx * (PanSlop / travel);
			_panStartY += dy * (PanSlop / travel);
			_panStartHOffset = HorizontalOffset;
			_panStartOffset = VerticalOffset;
			CapturePointer(_panPointer);
		}
		if (horizontally)
			SetHorizontalOffset(_panStartHOffset + (_panStartX - position.X));
		if (vertically)
			SetVerticalOffset(_panStartOffset + (_panStartY - position.Y));
		e.Handled = true;
	}

	private void OnPanReleased(object sender, RoutedEventArgs e)
	{
		if (!_panPressed || e is not PointerRoutedEventArgs p || p.PointerId != _panPointer)
			return;
		_panPressed = false;
		if (!_panning)
			return; // a tap: the control under the finger has it
		_panning = false;
		ReleasePointerCapture(_panPointer);
		e.Handled = true;
	}

	private void OnPanCaptureLost(object sender, RoutedEventArgs e)
	{
		if (ReferenceEquals(e.OriginalSource, this))
			_panPressed = _panning = false;
	}

	// The offsets that bring `element` wholly into the viewport (its top-left corner, when it is larger than the viewport)
	// with the least scrolling; null when it is no longer under this scroll viewer.
	private (float X, float Y)? OffsetsShowing(UIElement element)
	{
		// Its arranged places, up to the content, add up to its corner in viewport space: the content sits at minus the offsets.
		var left = 0f;
		var top = 0f;
		var node = element;
		for (; node is not null && !ReferenceEquals(node, this); node = node.Parent)
		{
			left += node.LayoutRect.X;
			top += node.LayoutRect.Y;
		}
		if (node is null)
		{
			_focused = null;
			return null;
		}

		return (
			OffsetShowing(left, element.LayoutRect.Width, ViewportWidth, HorizontalOffset, ScrollableWidth),
			OffsetShowing(top, element.LayoutRect.Height, ViewportHeight, VerticalOffset, ScrollableHeight));
	}

	// Along one axis: the offset that brings [start, start + length) in viewport space into a viewport of `viewport`, scrolled
	// as little as it can be from `offset`, its start winning when it can't all fit.
	private static float OffsetShowing(float start, float length, float viewport, float offset, float scrollable)
	{
		var showing = offset;
		if (start + length > viewport)
			showing += start + length - viewport;
		if (start - (showing - offset) < 0f)
			showing = offset + start;
		return Math.Clamp(showing, 0f, scrollable);
	}
}
