using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Extensions;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.Media.Imaging;
using SUIsei.Rasterizer;
using SUIsei.RoutedEvents;

namespace SUIsei.Tests;

/// <summary>
/// 2D hit-testing through the composition, with <see cref="UIRoot.HitTest"/> at a point in surface pixels: reverse-z
/// (top-most wins), <c>IsHitTestVisible</c>/<c>Visibility</c>, the composed visual transforms (render transforms,
/// storyboard-animated translates, layout scale), clips as the compositor draws them, and only where content draws,
/// per content kind. Plus WinUI's point mapping (<see cref="UIElement.TransformToVisual"/>,
/// <see cref="PointerRoutedEventArgs.GetCurrentPoint"/>), and no allocation per pointer move.
/// </summary>
public sealed class HitTestTests
{
	private static UIRoot Laid(UIElement content, float w = 200f, float h = 200f, float layoutScale = 1f)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content, LayoutScale = layoutScale };
		root.LayoutPass(new Size(w, h));
		return root;
	}

	private static Border Box(float w = 50f, float h = 50f) =>
		new() { Width = w, Height = h, Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) };

	private static Canvas CanvasOf(params UIElement[] children)
	{
		var canvas = new Canvas();
		foreach (var child in children)
			canvas.Children.Add(child);
		return canvas;
	}

	[Test]
	public void Reverse_z_returns_the_topmost_overlapping_element()
	{
		var bottom = Box();
		var top = Box();
		var root = Laid(CanvasOf(bottom, top)); // added last ⇒ drawn on top

		root.HitTest(new Vector2(10, 10)).ShouldBe(top);
	}

	[Test]
	public void IsHitTestVisible_false_skips_the_element_and_its_subtree()
	{
		var b = Box();
		b.IsHitTestVisible = false;
		var child = Box(20f, 20f);
		var parent = new Border { Width = 50f, Height = 50f, Child = child, IsHitTestVisible = false };
		Canvas.SetLeft(parent, 100f);

		var root = Laid(CanvasOf(b, parent));

		root.HitTest(new Vector2(10, 10)).ShouldBeNull();
		root.HitTest(new Vector2(110, 10)).ShouldBeNull();   // the child is visible to hit-testing, its parent is not
	}

	[Test]
	public void Collapsed_element_is_not_hit()
	{
		var b = Box();
		b.Visibility = Visibility.Collapsed;

		Laid(CanvasOf(b)).HitTest(new Vector2(10, 10)).ShouldBeNull();
	}

	[Test]
	public void A_transparent_panel_lets_the_point_fall_through()
	{
		Laid(new Canvas()).HitTest(new Vector2(10, 10)).ShouldBeNull(); // no background ⇒ no drawn content
	}

	[Test]
	public void A_translating_render_transform_moves_the_hit_region()
	{
		var b = Box();
		b.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateTranslation(100f, 0f) };
		var root = Laid(CanvasOf(b));

		root.HitTest(new Vector2(120f, 10f)).ShouldBe(b);   // shifted +100 in x
		root.HitTest(new Vector2(10f, 10f)).ShouldBeNull(); // its original cell is now empty
	}

	[Test]
	public void A_scaled_element_hits_over_the_area_it_is_drawn_at()
	{
		var b = Box();
		b.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateScale(2f) };
		var root = Laid(CanvasOf(b));

		root.HitTest(new Vector2(90f, 90f)).ShouldBe(b);    // inside the 100×100 it draws at, outside its 50×50 layout
		root.HitTest(new Vector2(110f, 10f)).ShouldBeNull();
	}

	[Test]
	public void A_rotated_element_hits_where_it_is_drawn_not_where_it_was_laid_out()
	{
		// 100×20 rotated a quarter turn about its origin, laid out at (50, 50): drawn over x∈[30,50], y∈[50,150].
		var b = Box(100f, 20f);
		b.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateRotation(MathF.PI / 2f) };
		Canvas.SetLeft(b, 50f);
		Canvas.SetTop(b, 50f);
		var root = Laid(CanvasOf(b));

		root.HitTest(new Vector2(40f, 120f)).ShouldBe(b);
		root.HitTest(new Vector2(120f, 55f)).ShouldBeNull();   // the unrotated layout rect
	}

	[Test]
	public void Translation_moves_the_hit_region_and_point_mapping_with_no_layout()
	{
		var b = Box();
		var root = Laid(CanvasOf(b));

		b.Translation = new Vector3(100f, 0f, 7f); // Z is kept but moves nothing in 2D
		b.ArrangeDirty.ShouldBeFalse();
		b.Visual.IsDirty.ShouldBeTrue();
		root.HitTest(new Vector2(120f, 10f)).ShouldBe(b);
		root.HitTest(new Vector2(10f, 10f)).ShouldBeNull();
		b.TransformToVisual(null).TransformPoint(new Point(0f, 0f)).ShouldBe(new Point(100f, 0f));

		// It composes after the render transform: a scale about the origin, then the offset.
		b.RenderTransform = new ScaleTransform { ScaleX = 2f, ScaleY = 2f };
		b.TransformToVisual(null).TransformPoint(new Point(10f, 10f)).ShouldBe(new Point(120f, 20f));
	}

	[Test]
	public void A_storyboard_animated_translate_moves_the_hit_region()
	{
		var b = Box();
		b.RenderTransform = new TranslateTransform();
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = CanvasOf(b);
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		host.Clock.Begin(new Storyboard
		{
			Children = { new DoubleAnimation { To = 100.0, Duration = TimeSpan.FromSeconds(1), Target = b, TargetProperty = new PropertyPath(UIElement.RenderTransformProperty, TranslateTransform.XProperty) } },
		});
		host.Tick(1.0, 0f, new Vector2(200f, 100f));

		view.Root.HitTest(new Vector2(120f, 10f)).ShouldBe(b);
		view.Root.HitTest(new Vector2(10f, 10f)).ShouldBeNull();
	}

	[Test]
	public void LayoutScale_maps_surface_pixels_to_DIPs()
	{
		var b = Box();
		var root = Laid(CanvasOf(b), w: 400f, h: 400f, layoutScale: 2f);

		root.HitTest(new Vector2(90f, 90f)).ShouldBe(b);       // 50 DIPs = 100 pixels
		root.HitTest(new Vector2(110f, 10f)).ShouldBeNull();
		root.HitTest(new Vector2(500f, 10f)).ShouldBeNull();   // outside the surface
	}

	[Test]
	public void Overlays_hit_above_content_through_the_same_scale()
	{
		var content = Box(200f, 200f);
		var overlay = Box(30f, 30f);
		var root = Laid(CanvasOf(content), w: 400f, h: 400f, layoutScale: 2f);
		root.AddOverlay(overlay, new Vector2(10f, 10f));
		root.LayoutPass(new Size(400f, 400f));

		root.HitTest(new Vector2(30f, 30f)).ShouldBe(overlay);  // overlay DIPs [10,40] = pixels [20,80]
		root.HitTest(new Vector2(100f, 100f)).ShouldBe(content);
	}

	[Test]
	public void ScrollViewer_overflow_is_not_hittable()
	{
		var tall = Box(80f, 500f);
		var viewer = new ScrollViewer { Content = tall, Width = 80f, Height = 100f };
		var root = Laid(CanvasOf(viewer), w: 200f, h: 300f);

		root.HitTest(new Vector2(40f, 50f)).ShouldBe(tall);
		root.HitTest(new Vector2(40f, 150f)).ShouldBeNull();   // laid out there, but clipped away by the viewer
	}

	[Test]
	public void An_image_hits_over_its_destination_rect_not_its_letterbox()
	{
		// A 2:1 image in a 100×100 box, stretched uniformly: drawn over y∈[25,75].
		var image = new Image { Source = BitmapSource.FromRgba8(2, 1, new byte[8]), Width = 100f, Height = 100f };
		var root = Laid(CanvasOf(image));

		root.HitTest(new Vector2(50f, 50f)).ShouldBe(image);
		root.HitTest(new Vector2(50f, 10f)).ShouldBeNull();
		root.HitTest(new Vector2(50f, 90f)).ShouldBeNull();
	}

	[Test]
	public void Under_a_rotated_clip_hits_agree_with_the_pixels_drawn()
	{
		// A 100×100 clipping container turned 45°, holding a child that overflows it on every side. The compositor clips
		// with the bounding box of the turned clip, so the child draws in the corners of that box outside the turned
		// square; hit-testing uses the same bound, so the pointer hits exactly what the user sees.
		const int W = 240, H = 200;
		var child = new Border { Width = 400f, Height = 400f, Background = new SolidColorBrush(HdrColor.FromRgb(0, 0, 255)) };
		Canvas.SetLeft(child, -150f);
		Canvas.SetTop(child, -150f);
		var container = CanvasOf(child);
		container.Width = 100f;
		container.Height = 100f;
		container.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateRotation(MathF.PI / 4f) };
		Clipping.SetClipToBounds(container, true);
		Canvas.SetLeft(container, 100f);
		Canvas.SetTop(container, 20f);
		var root = Laid(CanvasOf(container), W, H);

		var rasterizer = new SoftwareRasterizer(W, H);
		rasterizer.Clear(HdrColor.FromRgb(255, 255, 255).ToPremultiplied().ToVector4());
		rasterizer.Render(root.Render());
		var pixels = rasterizer.ResolveToRgba8();

		// Inside the clip's bounding box (x∈[29,171], y∈[20,161]) but outside the turned square; then the square's
		// centre; then outside the box.
		Vector2[] points = [new(35f, 25f), new(100f, 90f), new(20f, 100f), new(100f, 180f)];
		foreach (var p in points)
		{
			var offset = ((int)p.Y * W + (int)p.X) * 4;
			var drawn = pixels[offset + 2] > 200 && pixels[offset] < 50;   // blue, not the white background
			(root.HitTest(p) is not null).ShouldBe(drawn, $"at {p}");
		}
		root.HitTest(new Vector2(35f, 25f)).ShouldBe(child);
		root.HitTest(new Vector2(20f, 100f)).ShouldBeNull();
	}

	[Test]
	public void TransformToVisual_maps_through_the_composed_transforms_and_round_trips()
	{
		var inner = Box(20f, 20f);
		inner.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateTranslation(5f, 0f) };
		Canvas.SetLeft(inner, 10f);
		Canvas.SetTop(inner, 5f);
		var outer = CanvasOf(inner);
		outer.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateScale(2f) };
		var sibling = Box(10f, 10f);
		Canvas.SetLeft(sibling, 100f);
		Laid(CanvasOf(outer, sibling));

		var toRoot = inner.TransformToVisual(null);
		toRoot.TransformPoint(new Point(1f, 2f)).ShouldBe(new Point(32f, 14f));   // ((1+5+10)·2, (2+5)·2)
		toRoot.Inverse!.TransformPoint(new Point(32f, 14f)).ShouldBe(new Point(1f, 2f));

		var toSibling = inner.TransformToVisual(sibling);
		toSibling.TransformPoint(new Point(1f, 2f)).ShouldBe(new Point(-68f, 14f));
		toRoot.TransformBounds(new Rect(0f, 0f, 20f, 20f)).ShouldBe(new Rect(30f, 10f, 40f, 40f));

		Should.Throw<ArgumentException>(() => inner.TransformToVisual(new Border()));
	}

	[Test]
	public void GetCurrentPoint_gives_the_pointer_in_an_elements_space_and_the_roots()
	{
		// Root at 2 pixels per DIP; a canvas scaled ×2; a border laid out at (10, 5) and translated +5 in x. Border-local
		// (x, y) lands on surface pixel (4x + 60, 4y + 20).
		var border = Box();
		border.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateTranslation(5f, 0f) };
		Canvas.SetLeft(border, 10f);
		Canvas.SetTop(border, 5f);
		var canvas = CanvasOf(border);
		canvas.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateScale(2f) };
		var host = new UIHost();
		host.CreateRoot(new UILayer(0, 2f)).Root.Content = canvas;
		host.Tick(0.016, 0f, new Vector2(400f, 400f));

		Vector2? local = null, rootDip = null;
		border.AddHandler(UIElement.PointerPressedEvent, (_, e) =>
		{
			var p = (PointerRoutedEventArgs)e;
			local = p.GetCurrentPoint(border).Position;
			rootDip = p.GetCurrentPoint(null).Position;
		});
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(100f, 60f),
			PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 1f, Vector2.Zero, 0));
		host.Tick(0.016, 0f, new Vector2(400f, 400f));

		local.ShouldBe(new Vector2(10f, 10f));
		rootDip.ShouldBe(new Vector2(50f, 30f));
		border.TransformToVisual(null).TransformPoint(new Point(10f, 10f)).ShouldBe(new Point(50f, 30f));
	}

	// Press a slider's track at a surface point (and release), returning the value it took.
	private static double PressSlider(Matrix3x2 transform, Func<Slider, Vector2> surfacePoint)
	{
		var slider = new Slider { Minimum = 0d, Maximum = 100d, StepFrequency = 0d, Width = 100f };
		var canvas = CanvasOf(slider);
		canvas.RenderTransform = new MatrixTransform { Matrix = transform };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = canvas;
		host.Tick(0.016, 0f, new Vector2(400f, 200f));

		var at = surfacePoint(slider);
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, at, PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 1f, Vector2.Zero, 0));
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, at + new Vector2(20f, 0f), PointerUpdateKind.None, PointerButtons.Left, 1f, Vector2.Zero, 0));
		host.Tick(0.016, 0f, new Vector2(400f, 200f));
		return slider.Value;
	}

	[Test]
	public void Dragging_a_scaled_slider_maps_the_pointer_through_its_scale()
	{
		// Pressing at x, then dragging 20 pixels, on an unscaled slider must give what pressing at 2x and dragging 40
		// pixels gives on one drawn at twice the size; dragging 20 pixels there moves it half as far.
		var plain = PressSlider(Matrix3x2.Identity, s => new Vector2(40f, s.LayoutRect.Height / 2f));
		var scaled = PressSlider(Matrix3x2.CreateScale(2f), s => new Vector2(80f, s.LayoutRect.Height));

		plain.ShouldBeGreaterThan(0d);
		scaled.ShouldBeLessThan(plain);
		var scaledFurther = PressSlider(Matrix3x2.CreateScale(2f), s => new Vector2(80f + 20f, s.LayoutRect.Height));
		scaledFurther.ShouldBe(plain, 1e-6);
	}

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Pointer_moves_across_a_large_tree_allocate_nothing()
	{
		// 20 rows of 10 boxes, each row in its own panel, plus buttons: a move crosses many elements and enters and
		// leaves them, and must allocate nothing once warm.
		var rows = new StackPanel();
		for (var r = 0; r < 20; r++)
		{
			var row = new StackPanel { Orientation = Orientation.Horizontal };
			for (var c = 0; c < 10; c++)
				row.Children.Add(c % 3 == 0 ? new Button { Width = 18f, Height = 18f } : Box(18f, 18f));
			rows.Children.Add(row);
		}
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = new Border { Child = rows, Background = new SolidColorBrush(HdrColor.FromRgb(10, 10, 10)) };
		var size = new Vector2(400f, 400f);
		host.Tick(0.016, 0f, size);

		void Sweep()
		{
			for (var i = 0; i < 200; i++)
			{
				var p = new Vector2(i * 37 % 190, i * 53 % 370);
				host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, p, PointerUpdateKind.None, PointerButtons.None, 0f, Vector2.Zero, 0));
				host.Tick(0.016, 0f, size);
			}
		}

		Sweep();
		Sweep();
		Allocations.ShouldAllocateNothing(Sweep);
	}
}
