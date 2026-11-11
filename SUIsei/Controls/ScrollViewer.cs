using SUIsei.Extensions;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A clipping viewport over a single oversized <see cref="Content"/>. Measures the content
/// unbounded in the scroll axis to learn its extent, arranges it offset by the scroll position, and clips to the
/// viewport. Scrolls by mouse wheel, by keyboard (Page/Home/End/arrows when a descendant is focused), and
/// programmatically; the offset is always clamped to <see cref="ScrollableHeight"/>/<see cref="ScrollableWidth"/>.
/// Pairs with <see cref="VirtualizingStackPanel"/>, which reports its full extent for exactly this viewport.
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

	// The thumb fades between its brushes through its BrushTransition when on a live host, and switches at once when not.
	private readonly Border _vThumb = new()
	{
		Background = ThumbRest,
		CornerRadius = new CornerRadius(ThumbThickness / 2f),
		BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) },
	};
	// VisualChildren (the content, then the thumb), rebuilt only when Content changes, so reading it allocates nothing.
	private UIElement[] _visualChildren;
	private float _thumbHeight;
	private float _thumbTravel;
	private bool _thumbOver;
	private bool _draggingThumb;
	private float _dragStartPointerY;
	private float _dragStartOffset;
	// The descendant holding keyboard focus, kept in view: scrolled to when it takes focus and again whenever the viewport
	// shrinks under it (an on-screen keyboard opening over the page).
	private UIElement? _focused;
	private bool _bringFocusedIntoView;
	// Touch panning: a finger down on the content, and whether it has travelled far enough (PanSlop, in DIPs) to be a pan
	// rather than a tap; once it has, the viewer holds the pointer and the content follows the finger.
	private const float PanSlop = 8f;
	private bool _panPressed;
	private bool _panning;
	private uint _panPointer;
	private float _panStartY;
	private float _panStartOffset;

	public ScrollViewer()
	{
		ClipToBounds = true;
		_visualChildren = [_vThumb];
		AdoptChild(_vThumb);
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

		// The thumb is a grab handle: hovering/grabbing it lights it up, a press starts a drag (capturing the pointer
		// so it tracks past the thumb's edges), and the drag maps pointer travel onto the scroll range.
		_vThumb.AddHandler(PointerEnteredEvent, OnThumbEntered);
		_vThumb.AddHandler(PointerExitedEvent, OnThumbExited);
		_vThumb.AddHandler(PointerPressedEvent, OnThumbPressed);
		_vThumb.AddHandler(PointerMovedEvent, OnThumbMoved);
		_vThumb.AddHandler(PointerReleasedEvent, OnThumbReleased);
		_vThumb.AddHandler(PointerCaptureLostEvent, OnThumbCaptureLost);
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
			_visualChildren = value is null ? [_vThumb] : [value, _vThumb];
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

	public float ScrollableHeight => MathF.Max(0f, ExtentHeight - ViewportHeight);
	public float ScrollableWidth => MathF.Max(0f, ExtentWidth - ViewportWidth);

	/// <summary>The thumb's current interaction-state target colour (test seam): rest, hover, or grabbed.</summary>
	internal HdrColor ThumbStateColor => ((SolidColorBrush)_vThumb.Background!).Color;

	public void ScrollToVerticalOffset(float offset) => SetVerticalOffset(offset);

	public void ScrollToHorizontalOffset(float offset) => SetHorizontalOffset(offset);

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
		if (Content is null)
		{
			ExtentWidth = ExtentHeight = 0f;
			return Size.Zero;
		}

		// Measure the content unbounded in the scroll axis so its full extent is known regardless of the viewport.
		Content.Measure(new Size(available.Width, float.PositiveInfinity));
		ExtentWidth = Content.DesiredSize.Width;
		ExtentHeight = Content.DesiredSize.Height;

		// The viewport is the available size; an unbounded container collapses the viewport to the extent (no scroll).
		var width = float.IsPositiveInfinity(available.Width) ? ExtentWidth : available.Width;
		var height = float.IsPositiveInfinity(available.Height) ? ExtentHeight : available.Height;
		return new Size(width, height);
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
			// known once the content is arranged, so scroll then and arrange the content again at the new offset.
			if (_focused is not null && (_bringFocusedIntoView || shrank))
			{
				_bringFocusedIntoView = false;
				if (OffsetShowing(_focused) is { } offset && offset != VerticalOffset)
				{
					VerticalOffset = offset;
					content.Arrange(new Rect(-HorizontalOffset, -VerticalOffset, contentWidth, contentHeight));
				}
			}
		}

		ArrangeThumb(finalSize);
		return finalSize;
	}

	// Size the thumb to the visible fraction of the content and slide it down in proportion to the scroll position;
	// hide it when there is nothing to scroll.
	private void ArrangeThumb(Size finalSize)
	{
		if (ScrollableHeight > 0f && ViewportHeight > 0f)
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
	}

	// Brighten/dim the thumb toward its current interaction state. Grabbed wins over hover wins over rest; faded by the
	// thumb's BrushTransition when on a live host, switched at once when not (so the state is observable headlessly).
	private void UpdateThumbVisual()
	{
		_vThumb.Background = _draggingThumb ? ThumbActive : _thumbOver ? ThumbHover : ThumbRest;
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

	private void OnPointerWheelChanged(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs pointer)
			return;
		// Wheel up (positive delta) scrolls toward the top (offset decreases).
		SetVerticalOffset(VerticalOffset - pointer.Wheel.Y * Scrolling.GetWheelScrollAmount(this));
		e.Handled = true;
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key)
			return;
		switch (key.Key)
		{
			case VirtualKey.PageDown: SetVerticalOffset(VerticalOffset + ViewportHeight); e.Handled = true; break;
			case VirtualKey.PageUp: SetVerticalOffset(VerticalOffset - ViewportHeight); e.Handled = true; break;
			case VirtualKey.Home: SetVerticalOffset(0f); e.Handled = true; break;
			case VirtualKey.End: SetVerticalOffset(ScrollableHeight); e.Handled = true; break;
			case VirtualKey.Down: SetVerticalOffset(VerticalOffset + Scrolling.GetWheelScrollAmount(this)); e.Handled = true; break;
			case VirtualKey.Up: SetVerticalOffset(VerticalOffset - Scrolling.GetWheelScrollAmount(this)); e.Handled = true; break;
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

	// A finger down on the content may be the start of a pan. A mouse scrolls with its wheel and the thumb instead, and a
	// finger on the thumb drags the thumb.
	private void OnPanPressed(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs p || p.Device != PointerDeviceType.Touch || ScrollableHeight <= 0f
			|| ReferenceEquals(e.OriginalSource, _vThumb))
			return;
		_panPressed = true;
		_panning = false;
		_panPointer = p.PointerId;
		_panStartY = p.GetCurrentPoint(this).Position.Y;
		_panStartOffset = VerticalOffset;
	}

	// Past the slop, the press becomes a pan: the viewer takes the pointer, which ends the press on the control under the
	// finger without a click, and from then on the content follows the finger.
	private void OnPanMoved(object sender, RoutedEventArgs e)
	{
		if (!_panPressed || e is not PointerRoutedEventArgs p || p.PointerId != _panPointer)
			return;
		var y = p.GetCurrentPoint(this).Position.Y;
		if (!_panning)
		{
			if (MathF.Abs(y - _panStartY) < PanSlop)
				return;
			_panning = true;
			// Follow the finger from the slop's edge, so the content neither jumps by the slop nor loses travel past it that a
			// coalesced move already carries.
			_panStartY += MathF.CopySign(PanSlop, y - _panStartY);
			_panStartOffset = VerticalOffset;
			CapturePointer(_panPointer);
		}
		SetVerticalOffset(_panStartOffset + (_panStartY - y));
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

	// The vertical offset that brings `element` wholly into the viewport (its top, when it is taller than the viewport) with
	// the least scrolling; null when it is no longer under this scroll viewer.
	private float? OffsetShowing(UIElement element)
	{
		// Its arranged places, up to the content, add up to its top in viewport space: the content sits at minus the offset.
		var top = 0f;
		var node = element;
		for (; node is not null && !ReferenceEquals(node, this); node = node.Parent)
			top += node.LayoutRect.Y;
		if (node is null)
		{
			_focused = null;
			return null;
		}

		var bottom = top + element.LayoutRect.Height;
		var offset = VerticalOffset;
		if (bottom > ViewportHeight)
			offset += bottom - ViewportHeight;
		if (top - (offset - VerticalOffset) < 0f)
			offset = VerticalOffset + top;
		return Math.Clamp(offset, 0f, ScrollableHeight);
	}
}
