using System.Numerics;

using SUIsei.Controls;
using SUIsei.Input;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="ScrollViewer"/> (PlanningUI.md MU6): reports extent/viewport/scrollable range, offsets its content
/// by the scroll position clamped to range, clips to the viewport, and scrolls on a wheel event.
/// </summary>
public sealed class ScrollViewerTests
{
	private static (ScrollViewer Viewer, UIRoot Root) Build(float viewport = 100f, float content = 500f)
	{
		var child = new Border { Width = 80f, Height = content, Background = HdrColor.FromRgb(50, 50, 50) };
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
		var child = new Border { Width = 80f, Height = 500f, Background = HdrColor.FromRgb(50, 50, 50) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UIPlacement.Overlay(0, 1f)).Root.Content = viewer;
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
		var child = new Border { Width = 80f, Height = 500f, Background = HdrColor.FromRgb(50, 50, 50) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UIPlacement.Overlay(0, 1f)).Root.Content = viewer;
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
		var child = new Border { Width = 80f, Height = 500f, Background = HdrColor.FromRgb(50, 50, 50) };
		var viewer = new ScrollViewer { Content = child, Width = 80f, Height = 100f };
		var host = new UIHost();
		host.CreateRoot(UIPlacement.Overlay(0, 1f)).Root.Content = viewer;
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
}
