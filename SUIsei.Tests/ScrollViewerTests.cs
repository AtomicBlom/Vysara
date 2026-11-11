using System.Numerics;
using SUIsei.Controls;
using SUIsei.Extensions;
using SUIsei.Input;
using SUIsei.Media;

namespace SUIsei.Tests;

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
