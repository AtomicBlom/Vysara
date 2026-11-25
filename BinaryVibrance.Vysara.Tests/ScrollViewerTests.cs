using System.Numerics;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Extensions;
using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="ScrollViewer"/>: reports extent/viewport/scrollable range, offsets its content
/// by the scroll position clamped to range, clips to the viewport, and scrolls on a wheel event.
/// </summary>
public sealed class ScrollViewerTests
{
	private static (ScrollViewer Viewer, UIRoot Root) Build(float viewport = 100f, float content = 500f)
	{
		var child = new Border { Width = 80f, Height = content, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = viewport };
		var root = new UIRoot(UISurface.Hud) { Content = viewer };
		root.LayoutPass(new Size(80f, viewport));
		return (viewer, root);
	}

	[Test]
	public void Reports_extent_viewport_and_scrollable_height()
	{
		var (viewer, _) = Build(100f, 500f);

		viewer.ExtentHeight.ShouldBe(500f);
		viewer.ViewportHeight.ShouldBe(100f);
		viewer.ScrollableHeight.ShouldBe(400f);
	}

	[Test]
	public void Scrolls_and_clamps_to_range()
	{
		var (viewer, root) = Build(100f, 500f);

		viewer.ScrollToVerticalOffset(150f);
		root.LayoutPass(new Size(80f, 100f));
		viewer.VerticalOffset.ShouldBe(150f);

		viewer.ScrollToVerticalOffset(99999f);
		root.LayoutPass(new Size(80f, 100f));
		viewer.VerticalOffset.ShouldBe(400f);   // clamped to ScrollableHeight

		viewer.ScrollToVerticalOffset(-50f);
		viewer.VerticalOffset.ShouldBe(0f);
	}

	[Test]
	public void Offsets_its_content_and_clips_to_the_viewport()
	{
		var (viewer, root) = Build(100f, 500f);

		viewer.ScrollToVerticalOffset(120f);
		root.LayoutPass(new Size(80f, 100f));

		viewer.Content!.LayoutRect.Y.ShouldBe(-120f);
		viewer.Visual.Clip.ShouldNotBeNull();
		viewer.Visual.Clip!.Value.Height.ShouldBe(100f);
	}

	[Test]
	public void Wheel_scrolls_the_viewer()
	{
		var child = new Border { Width = 80f, Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		host.Tick(0.016, 0f, new Vector2(80f, 100f));

		// Wheel down (negative delta) over the content scrolls toward the bottom (offset increases).
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(40f, 50f),
			PointerUpdateKind.None, PointerButtons.None, 1f, new Vector2(0f, -1f), 0));
		host.Tick(0.016, 0f, new Vector2(80f, 100f));

		viewer.VerticalOffset.ShouldBeGreaterThan(0f);
	}

	// A part held still over the content by a transform set as the view changes is drawn where the content is, in the frame
	// the wheel turned: the content is arranged at the new offset before the frame is drawn, not a frame behind.
	[Test]
	public void A_wheel_is_arranged_in_the_frame_it_turned()
	{
		var stay = new TranslateTransform();
		var head = new Border { Width = 80f, Height = 10f, RenderTransform = stay };
		var child = new StackPanel { Width = 80f };
		child.Children.Add(head);
		child.Children.Add(new Border { Width = 80f, Height = 490f });
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		viewer.ViewChanged += (_, _) => stay.Y = viewer.VerticalOffset;
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		host.Tick(0.016, 0f, new Vector2(80f, 100f));

		host.QueuePointer(PointerInput.Scroll(new Vector2(40f, 50f), new Vector2(0f, -1f)));
		host.Tick(0.016, 0f, new Vector2(80f, 100f));

		viewer.VerticalOffset.ShouldBeGreaterThan(0f);
		child.LayoutRect.Y.ShouldBe(-viewer.VerticalOffset);
		UiDriver.Bounds(head).Y.ShouldBe(0f);
	}

	// A root that rounds its layout at 1.5 pixels a DIP, a 100-DIP viewer over 500 DIPs, ticked once
	private static (ScrollViewer Viewer, UIHost Host) Rounded()
	{
		var child = new Border { Width = 80f, Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		var root = host.CreateRoot(UILayer.Default).Root;
		root.UseLayoutRounding = true;
		root.LayoutScale = 1.5f;
		root.Content = viewer;
		host.Tick(0.016, 0f, new Vector2(120f, 150f));
		return (viewer, host);
	}

	// Rounded, the offsets keep to whole pixels, so the content sits exactly at minus the offset and what follows the offset
	// with a transform lines up with it, not half a pixel off
	[Test]
	public void Rounded_the_offsets_keep_to_whole_pixels()
	{
		var (viewer, host) = Rounded();

		viewer.ScrollToVerticalOffset(10.2f);
		host.Tick(0.016, 0f, new Vector2(120f, 150f));
		(viewer.VerticalOffset * 1.5f).ShouldBe(MathF.Round(viewer.VerticalOffset * 1.5f), 1e-4f);
		viewer.Content!.LayoutRect.Y.ShouldBe(-viewer.VerticalOffset, 1e-4f);

		host.QueuePointer(PointerInput.Scroll(new Vector2(40f, 50f), new Vector2(0f, -0.137f)));
		host.Tick(0.016, 0f, new Vector2(120f, 150f));
		(viewer.VerticalOffset * 1.5f).ShouldBe(MathF.Round(viewer.VerticalOffset * 1.5f), 1e-4f);
		viewer.Content!.LayoutRect.Y.ShouldBe(-viewer.VerticalOffset, 1e-4f);
	}

	// A trackpad's deltas, each too small to reach the next pixel, still scroll between them: what a pixel didn't take is
	// carried to the next
	[Test]
	public void Rounded_small_wheel_deltas_add_up()
	{
		var (viewer, host) = Rounded();
		var amount = Scrolling.GetWheelScrollAmount(viewer);

		for (var i = 0; i < 40; i++)
		{
			host.QueuePointer(PointerInput.Scroll(new Vector2(40f, 50f), new Vector2(0f, -0.1f / amount)));
			host.Tick(0.016, 0f, new Vector2(120f, 150f));
		}

		viewer.VerticalOffset.ShouldBe(4f, 1f / 1.5f);
	}

	private static readonly Vector2 Square = new(100f, 100f);

	// A 100 × 100 viewer over a `width` × `height` block, its horizontal scroll bar as given, on a host ticked once.
	private static (ScrollViewer Viewer, UIHost Host) Sideways(float width, float height, ScrollBarVisibility horizontal = ScrollBarVisibility.Auto)
	{
		var child = new Border { Width = width, Height = height, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 100f, Height = 100f, HorizontalScrollBarVisibility = horizontal };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		host.Tick(0.016, 0f, Square);
		return (viewer, host);
	}

	private static void Wheel(UIHost host, Vector2 at, Vector2 delta, Vector2 surface)
	{
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, at, PointerUpdateKind.None, PointerButtons.None, 1f, delta, 0));
		host.Tick(0.016, 0f, surface);
	}

	[Test]
	public void Measures_its_content_to_the_viewport_width_unless_its_horizontal_scroll_bar_is_enabled()
	{
		var child = new Border { Width = 400f, Height = 50f };
		var viewer = new ScrollViewer { Content = child, Width = 100f, Height = 100f };
		var root = new UIRoot(UISurface.Hud) { Content = viewer };
		root.LayoutPass(new Size(100f, 100f));
		viewer.HorizontalScrollMode.ShouldBe(ScrollMode.Enabled); // as WinUI's: a finger may pan, but nothing is laid out to pan
		viewer.ScrollableWidth.ShouldBe(0f);

		viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
		root.LayoutPass(new Size(100f, 100f));
		viewer.ExtentWidth.ShouldBe(400f);
		viewer.ScrollableWidth.ShouldBe(300f);

		viewer.ScrollToHorizontalOffset(120f);
		root.LayoutPass(new Size(100f, 100f));
		child.LayoutRect.X.ShouldBe(-120f); // the content slides left by the offset

		viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		root.LayoutPass(new Size(100f, 100f));
		viewer.HorizontalOffset.ShouldBe(0f);
	}

	[Test]
	public void A_disabled_vertical_scroll_bar_does_not_scroll_up_and_down()
	{
		var (viewer, root) = Build(100f, 500f);
		viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
		root.LayoutPass(new Size(80f, 100f));

		viewer.ScrollableHeight.ShouldBe(0f);
		viewer.ScrollToVerticalOffset(150f);
		viewer.VerticalOffset.ShouldBe(0f);
	}

	[Test]
	public void A_wheel_scrolls_sideways_when_there_is_nothing_to_scroll_down()
	{
		var (viewer, host) = Sideways(400f, 50f);
		viewer.ScrollableHeight.ShouldBe(0f);

		// The content is 50 tall, centred in the 100 of the viewport
		Wheel(host, new Vector2(40f, 50f), new Vector2(0f, -1f), Square);
		viewer.HorizontalOffset.ShouldBeGreaterThan(0f);
		viewer.VerticalOffset.ShouldBe(0f);
	}

	[Test]
	public void A_sideways_wheel_scrolls_sideways_when_both_can_scroll()
	{
		var (viewer, host) = Sideways(400f, 500f);

		Wheel(host, new Vector2(40f, 50f), new Vector2(-1f, 0f), Square);
		viewer.HorizontalOffset.ShouldBeGreaterThan(0f);
		viewer.VerticalOffset.ShouldBe(0f);
	}

	[Test]
	public void A_wheel_the_viewer_cannot_scroll_any_further_goes_on_to_the_viewer_around_it()
	{
		var inner = new ScrollViewer
		{
			Height = 100f,
			Content = new Border { Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) },
		};
		var stack = new StackPanel();
		stack.Children.Add(inner);
		stack.Children.Add(new Border { Height = 300f });
		var outer = new ScrollViewer { Content = stack, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = outer;
		var surface = new Vector2(80f, 100f);
		host.Tick(0.016, 0f, surface);

		Wheel(host, new Vector2(40f, 50f), new Vector2(0f, -1f), surface);
		inner.VerticalOffset.ShouldBeGreaterThan(0f);
		outer.VerticalOffset.ShouldBe(0f);

		inner.ScrollToVerticalOffset(inner.ScrollableHeight);
		host.Tick(0.016, 0f, surface);
		Wheel(host, new Vector2(40f, 50f), new Vector2(0f, -1f), surface);
		outer.VerticalOffset.ShouldBeGreaterThan(0f);
	}

	[Test]
	public void A_scroll_bar_takes_a_gutter_from_the_viewport_and_the_content_stays_clear_of_its_thumb()
	{
		var child = new Border { Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var root = new UIRoot(UISurface.Hud) { Content = viewer };
		root.LayoutPass(new Size(80f, 100f));

		viewer.ViewportWidth.ShouldBeLessThan(80f);
		child.LayoutRect.Width.ShouldBe(viewer.ViewportWidth); // measured and arranged short of the gutter
		viewer.VerticalThumbRect.Height.ShouldBeGreaterThan(0f);
		viewer.VerticalThumbRect.X.ShouldBeGreaterThanOrEqualTo(child.LayoutRect.Right);

		// Nothing to scroll: no thumb, and the content has the whole width.
		child.Height = 50f;
		root.LayoutPass(new Size(80f, 100f));
		viewer.ViewportWidth.ShouldBe(80f);
		child.LayoutRect.Width.ShouldBe(80f);
	}

	[Test]
	public void A_viewer_sized_to_its_content_makes_room_for_its_horizontal_bar_below_it()
	{
		var child = new Border { Width = 400f, Height = 30f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer
		{
			Content = child,
			Width = 100f,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
		};
		var stack = new StackPanel();
		stack.Children.Add(viewer);
		var root = new UIRoot(UISurface.Hud) { Content = stack };
		root.LayoutPass(new Size(100f, 200f));

		viewer.ViewportHeight.ShouldBe(30f);
		viewer.LayoutRect.Height.ShouldBeGreaterThan(30f);
		viewer.HorizontalThumbRect.Y.ShouldBeGreaterThanOrEqualTo(30f); // under the content, not over it
	}

	[Test]
	public void A_viewer_that_finds_it_needs_its_bar_only_when_arranged_still_keeps_its_content_clear_of_it()
	{
		var items = new StackPanel();
		for (var i = 0; i < 20; i++)
			items.Children.Add(new Border { Height = 30f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) });
		var viewer = new ScrollViewer { Content = items };

		// Measured with all the height it wants, as a parent sizing it to its content does, then arranged shorter
		viewer.Measure(new Size(100f, float.PositiveInfinity));
		viewer.Arrange(new Rect(0f, 0f, 100f, 100f));

		viewer.ViewportWidth.ShouldBeLessThan(100f);
		items.LayoutRect.Width.ShouldBe(viewer.ViewportWidth);
		items.Children[0].LayoutRect.Width.ShouldBe(viewer.ViewportWidth);
	}

	[Test]
	public void A_wheel_over_a_gap_between_the_items_scrolls_the_viewer()
	{
		var items = new StackPanel();
		for (var i = 0; i < 10; i++)
			items.Children.Add(new Border { Height = 30f, Margin = new Thickness(0f, 0f, 0f, 20f), Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) });
		var viewer = new ScrollViewer { Content = items, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(80f, 100f);
		host.Tick(0.016, 0f, surface);

		Wheel(host, new Vector2(40f, 40f), new Vector2(0f, -1f), surface); // between the first item and the second
		viewer.VerticalOffset.ShouldBeGreaterThan(0f);
	}

	// A 100 × 100 viewer over a stack: a block, a viewer that scrolls only sideways, then a tall block.
	private static (ScrollViewer Outer, ScrollViewer Inner, UIHost Host) OverSideways()
	{
		var inner = new ScrollViewer
		{
			Height = 40f,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = new Border { Width = 600f, Background = new SolidColorBrush(HdrColor.FromRgb(90, 90, 90)) },
		};
		var stack = new StackPanel();
		stack.Children.Add(new Border { Height = 60f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) });
		stack.Children.Add(inner);
		stack.Children.Add(new Border { Height = 600f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) });
		var outer = new ScrollViewer { Content = stack, Width = 100f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = outer;
		host.Tick(0.016, 0f, Square);
		return (outer, inner, host);
	}

	[Test]
	public void A_wheel_gesture_goes_on_scrolling_what_it_began_on_as_another_viewer_slides_under_the_pointer()
	{
		var (outer, inner, host) = OverSideways();

		// A trackpad's two fingers: the pointer holds still while the content slides up under it, past the sideways viewer.
		for (var i = 0; i < 12; i++)
			Wheel(host, new Vector2(40f, 30f), new Vector2(0f, -0.25f), Square);

		outer.VerticalOffset.ShouldBeGreaterThan(100f);
		inner.HorizontalOffset.ShouldBe(0f);
	}

	[Test]
	public void Moving_the_pointer_ends_a_wheel_gesture()
	{
		var (outer, inner, host) = OverSideways();
		Wheel(host, new Vector2(40f, 30f), new Vector2(0f, -0.25f), Square);
		var scrolled = outer.VerticalOffset;
		scrolled.ShouldBeGreaterThan(0f);

		// Moved onto the sideways viewer, a new gesture scrolls it.
		var over = new Vector2(40f, 80f - scrolled);
		host.QueuePointer(PointerInput.Move(over));
		host.Tick(0.016, 0f, Square);
		Wheel(host, over, new Vector2(0f, -0.25f), Square);

		inner.HorizontalOffset.ShouldBeGreaterThan(0f);
		outer.VerticalOffset.ShouldBe(scrolled);
	}

	[Test]
	public void Dragging_the_horizontal_thumb_scrolls_sideways()
	{
		var (viewer, host) = Sideways(400f, 50f);
		var thumb = viewer.HorizontalThumbRect;
		thumb.Width.ShouldBeGreaterThan(0f);
		var grab = new Vector2(thumb.X + thumb.Width / 2f, thumb.Y + thumb.Height / 2f);

		host.QueuePointer(PointerInput.Press(grab));
		host.Tick(0.016, 0f, Square);
		host.QueuePointer(PointerInput.Move(grab + new Vector2(30f, 0f), PointerButtons.Left));
		host.Tick(0.016, 0f, Square);
		viewer.HorizontalOffset.ShouldBeGreaterThan(0f);
		viewer.VerticalOffset.ShouldBe(0f);

		// Releasing ends the drag: a later move no longer scrolls.
		host.QueuePointer(PointerInput.Release(grab + new Vector2(30f, 0f)));
		host.Tick(0.016, 0f, Square);
		var afterRelease = viewer.HorizontalOffset;
		host.QueuePointer(PointerInput.Move(grab + new Vector2(60f, 0f), PointerButtons.None));
		host.Tick(0.016, 0f, Square);
		viewer.HorizontalOffset.ShouldBe(afterRelease);
	}

	[Test]
	public void A_hidden_horizontal_scroll_bar_scrolls_sideways_with_no_thumb()
	{
		var (viewer, host) = Sideways(400f, 50f, ScrollBarVisibility.Hidden);

		viewer.ScrollableWidth.ShouldBe(300f);
		viewer.HorizontalThumbRect.Width.ShouldBe(0f);
		Wheel(host, new Vector2(40f, 50f), new Vector2(0f, -1f), Square);
		viewer.HorizontalOffset.ShouldBeGreaterThan(0f);
	}

	// A 100 × 100 viewer scrolling sideways over a 400-wide button: a finger presses at x = 80 and drags left to x = 30.
	private static (ScrollViewer Viewer, int Clicks) DragLeft(ScrollMode mode)
	{
		var button = new Button { Width = 400f, Height = 100f };
		var clicks = 0;
		button.Click += (_, _) => clicks++;
		var viewer = new ScrollViewer
		{
			Content = button,
			Width = 100f,
			Height = 100f,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = mode,
		};
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var t = 0f;
		void Step(PointerInput input)
		{
			host.QueuePointer(input);
			host.Tick(0.016, t += 0.016f, Square);
		}

		host.Tick(0.016, t, Square);
		const PointerDeviceType touch = PointerDeviceType.Touch;
		Step(PointerInput.Move(new Vector2(80f, 50f), PointerButtons.None, 0, touch));
		Step(PointerInput.Press(new Vector2(80f, 50f), PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 0, touch));
		Step(PointerInput.Move(new Vector2(60f, 50f), PointerButtons.Left, 0, touch));
		Step(PointerInput.Move(new Vector2(30f, 50f), PointerButtons.Left, 0, touch));
		Step(PointerInput.Release(new Vector2(30f, 50f), PointerUpdateKind.LeftButtonReleased, PointerButtons.None, 0, touch));
		return (viewer, clicks);
	}

	[Test]
	public void A_finger_pans_sideways_where_the_horizontal_scroll_mode_lets_it()
	{
		var (viewer, clicks) = DragLeft(ScrollMode.Enabled);
		viewer.HorizontalOffset.ShouldBe(42f); // 50 travelled, less the 8 of slop before the drag became a pan
		clicks.ShouldBe(0);

		var (unpanned, _) = DragLeft(ScrollMode.Disabled);
		unpanned.HorizontalOffset.ShouldBe(0f);
	}

	[Test]
	public void Right_and_Left_scroll_sideways_only_when_there_is_something_to_scroll()
	{
		var button = new Button { Width = 40f, Height = 40f };
		var row = new StackPanel { Orientation = Orientation.Horizontal };
		row.Children.Add(button);
		row.Children.Add(new Border { Width = 400f, Height = 40f });
		var viewer = new ScrollViewer { Content = row, Width = 100f, Height = 100f, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
		var outer = new Border { Child = viewer };
		var unhandled = 0;
		outer.AddHandler(UIElement.KeyDownEvent, (_, _) => unhandled++);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = outer;
		var t = 0f;
		host.Tick(0.016, t, Square);
		host.Focus.TrySetFocus(button, FocusState.Programmatic).ShouldBeTrue();
		void Press(VirtualKey key)
		{
			host.QueueKey(new KeyInput(key, Down: true, Repeat: false, ModifierKeys.None, 0));
			host.Tick(0.016, t += 0.016f, Square);
		}

		Press(VirtualKey.Right);
		viewer.HorizontalOffset.ShouldBeGreaterThan(0f);
		Press(VirtualKey.Left);
		viewer.HorizontalOffset.ShouldBe(0f);
		unhandled.ShouldBe(0);

		viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		host.Tick(0.016, t += 0.016f, Square);
		Press(VirtualKey.Right);
		unhandled.ShouldBe(1); // left for focus to move by
	}

	[Test]
	public void Focusing_a_descendant_right_of_the_viewport_scrolls_it_into_view()
	{
		var box = new TextBox { Width = 40f };
		var row = new StackPanel { Orientation = Orientation.Horizontal };
		row.Children.Add(new Border { Width = 300f });
		row.Children.Add(box);
		var viewer = new ScrollViewer { Content = row, Width = 100f, Height = 80f, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(100f, 80f);
		host.Tick(0.016, 0f, surface);

		host.Focus.TrySetFocus(box, FocusState.Programmatic).ShouldBeTrue();
		host.Tick(0.016, 0.016f, surface);

		viewer.HorizontalOffset.ShouldBe(240f); // the box's right edge, 340, at the viewport's, 100
	}

	[Test]
	public void Dragging_the_thumb_scrolls_the_viewer()
	{
		var child = new Border { Width = 80f, Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(80f, 100f);
		host.Tick(0.016, 0f, surface);

		// Press on the thumb (right edge, near the top where it rests at offset 0), drag down, release.
		host.QueuePointer(PointerInput.Press(new Vector2(74f, 8f)));
		host.Tick(0.016, 0f, surface);
		host.QueuePointer(PointerInput.Move(new Vector2(74f, 48f), PointerButtons.Left));
		host.Tick(0.016, 0f, surface);

		viewer.VerticalOffset.ShouldBeGreaterThan(0f);

		// Releasing ends the drag (capture released): a later move no longer scrolls.
		host.QueuePointer(PointerInput.Release(new Vector2(74f, 48f)));
		host.Tick(0.016, 0f, surface);
		var afterRelease = viewer.VerticalOffset;
		host.QueuePointer(PointerInput.Move(new Vector2(74f, 90f), PointerButtons.None));
		host.Tick(0.016, 0f, surface);
		viewer.VerticalOffset.ShouldBe(afterRelease);
	}

	[Test]
	public void Thumb_brightens_on_hover_and_while_grabbed()
	{
		var child = new Border { Width = 80f, Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(80f, 100f);
		host.Tick(0.016, 0f, surface);

		var rest = viewer.ThumbStateColor;

		// Hover the thumb → brighter.
		host.QueuePointer(PointerInput.Move(new Vector2(74f, 8f)));
		host.Tick(0.016, 0f, surface);
		var hover = viewer.ThumbStateColor;
		hover.R.ShouldBeGreaterThan(rest.R);

		// Grab it → brighter still.
		host.QueuePointer(PointerInput.Press(new Vector2(74f, 8f)));
		host.Tick(0.016, 0f, surface);
		viewer.ThumbStateColor.R.ShouldBeGreaterThan(hover.R);

		// Release while still over → back to the hover level.
		host.QueuePointer(PointerInput.Release(new Vector2(74f, 8f)));
		host.Tick(0.016, 0f, surface);
		viewer.ThumbStateColor.R.ShouldBe(hover.R);

		// Move away → back to rest.
		host.QueuePointer(PointerInput.Move(new Vector2(10f, 50f)));
		host.Tick(0.016, 0f, surface);
		viewer.ThumbStateColor.R.ShouldBe(rest.R);
	}

	// A 300-tall block above a 40-tall text box, so the box spans 300 to 340 in the content.
	private static (StackPanel Content, TextBox Box) BlockOverTextBox()
	{
		var box = new TextBox { Height = 40f };
		var content = new StackPanel();
		content.Children.Add(new Border { Height = 300f });
		content.Children.Add(box);
		return (content, box);
	}

	[Test]
	public void Focusing_a_descendant_below_the_viewport_scrolls_it_into_view()
	{
		var (content, box) = BlockOverTextBox();
		var viewer = new ScrollViewer { Content = content, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(80f, 100f);
		host.Tick(0.016, 0f, surface);
		viewer.VerticalOffset.ShouldBe(0f);

		host.Focus.TrySetFocus(box, FocusState.Programmatic).ShouldBeTrue();
		host.Tick(0.016, 0.016f, surface);

		viewer.VerticalOffset.ShouldBe(240f); // the box's bottom, 340, at the viewport's bottom, 100
	}

	[Test]
	public void The_focused_element_is_scrolled_back_into_view_when_a_keyboard_shrinks_the_viewport()
	{
		var (content, box) = BlockOverTextBox();
		var viewer = new ScrollViewer { Content = content };
		InputPane.SetAvoidsKeyboard(viewer, true);
		var host = new UIHost();
		var root = host.CreateRoot(UILayer.Default).Root;
		root.Content = viewer;
		var surface = new Vector2(80f, 400f);
		host.Focus.TrySetFocus(box, FocusState.Programmatic).ShouldBeTrue();
		host.Tick(0.016, 0f, surface);
		viewer.VerticalOffset.ShouldBe(0f); // all of it fits

		root.InputPaneHeight = 200f; // the keyboard covers the bottom half, so the viewport is 200 tall
		host.Tick(0.016, 0.016f, surface);

		viewer.ViewportHeight.ShouldBe(200f);
		viewer.VerticalOffset.ShouldBe(140f); // the box's bottom, 340, at the viewport's new bottom, 200
	}

	// A 100-tall viewer over a 500-tall button, ticked once: a press-drag-release by `device` from y=80 up to y=30.
	private static (ScrollViewer Viewer, int Clicks) DragUp(PointerDeviceType device)
	{
		var button = new Button { Width = 80f, Height = 500f };
		var clicks = 0;
		button.Click += (_, _) => clicks++;
		var viewer = new ScrollViewer { Content = button, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(80f, 100f);
		var t = 0f;
		void Step(PointerInput input)
		{
			host.QueuePointer(input);
			host.Tick(0.016, t += 0.016f, surface);
		}

		host.Tick(0.016, t, surface);
		Step(PointerInput.Move(new Vector2(40f, 80f), PointerButtons.None, 0, device));
		Step(PointerInput.Press(new Vector2(40f, 80f), PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 0, device));
		Step(PointerInput.Move(new Vector2(40f, 60f), PointerButtons.Left, 0, device));
		Step(PointerInput.Move(new Vector2(40f, 30f), PointerButtons.Left, 0, device));
		Step(PointerInput.Release(new Vector2(40f, 30f), PointerUpdateKind.LeftButtonReleased, PointerButtons.None, 0, device));
		return (viewer, clicks);
	}

	[Test]
	public void A_finger_dragging_the_content_pans_it_and_clicks_nothing()
	{
		var (viewer, clicks) = DragUp(PointerDeviceType.Touch);

		viewer.VerticalOffset.ShouldBe(42f); // 50 travelled, less the 8 of slop before the drag became a pan
		clicks.ShouldBe(0);
	}

	[Test]
	public void A_mouse_dragging_the_content_does_not_pan_it()
	{
		var (viewer, _) = DragUp(PointerDeviceType.Mouse);

		viewer.VerticalOffset.ShouldBe(0f);
	}

	[Test]
	public void A_finger_tap_without_travel_still_clicks()
	{
		var button = new Button { Width = 80f, Height = 500f };
		var clicks = 0;
		button.Click += (_, _) => clicks++;
		var viewer = new ScrollViewer { Content = button, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		var surface = new Vector2(80f, 100f);
		host.Tick(0.016, 0f, surface);
		host.QueuePointer(PointerInput.Move(new Vector2(40f, 50f), PointerButtons.None, 0, PointerDeviceType.Touch));
		host.QueuePointer(PointerInput.Press(new Vector2(40f, 50f), PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 0, PointerDeviceType.Touch));
		host.Tick(0.016, 0.016f, surface);
		host.QueuePointer(PointerInput.Move(new Vector2(40f, 53f), PointerButtons.Left, 0, PointerDeviceType.Touch));
		host.QueuePointer(PointerInput.Release(new Vector2(40f, 53f), PointerUpdateKind.LeftButtonReleased, PointerButtons.None, 0, PointerDeviceType.Touch));
		host.Tick(0.016, 0.032f, surface);

		viewer.VerticalOffset.ShouldBe(0f);
		clicks.ShouldBe(1);
	}
}
