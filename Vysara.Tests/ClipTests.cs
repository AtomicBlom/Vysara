using System.Numerics;
using Vysara.Animation;
using Vysara.Controls;
using Vysara.Drawing;
using Vysara.Extensions;
using Vysara.Media;
using Vysara.Rasterizer;

namespace Vysara.Tests;

/// <summary>
/// <see cref="UIElement.Clip"/>, WinUI's clip: it clips what the element's subtree draws and where it is hit, together
/// with <c>vysara:Clipping.ClipToBounds</c>; a change to the geometry applies with no relayout; and the element listens
/// to the geometry only while it is in a live tree.
/// </summary>
public sealed class ClipTests
{
	private static (Border Border, UIRoot Root) Laid(RectangleGeometry? clip, bool clipToBounds = false)
	{
		var border = new Border { Width = 40f, Height = 40f, Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)), Clip = clip };
		if (clipToBounds)
			Clipping.SetClipToBounds(border, true);
		var canvas = new Canvas();
		canvas.Children.Add(border);
		Canvas.SetLeft(border, 10f);
		Canvas.SetTop(border, 10f);
		var root = new UIRoot(new DrawListId(0)) { Content = canvas };
		root.LayoutPass(new Size(100f, 100f));
		return (border, root);
	}

	[Test]
	public void The_clip_rectangle_is_in_the_elements_own_coordinates_and_clips_its_drawing()
	{
		var (_, root) = Laid(new RectangleGeometry { Rect = new Rect(5, 5, 20, 10) });

		var draw = root.Render();
		draw.Commands.Span.ToArray().ShouldAllBe(c => c.ClipRect == new Rect(15, 15, 20, 10));

		var raster = new SoftwareRasterizer(100, 100);
		raster.Clear(Vector4.Zero);
		raster.Render(draw);
		var px = raster.ResolveToRgba8();
		Alpha(px, 20, 20).ShouldBe((byte)255); // inside the clip
		Alpha(px, 12, 12).ShouldBe((byte)0);   // inside the border, outside the clip
		Alpha(px, 40, 30).ShouldBe((byte)0);
	}

	[Test]
	[Arguments(false, 5f, 5f, 20f, 10f)]
	[Arguments(true, 0f, 0f, 20f, 20f)]
	public void With_ClipToBounds_the_element_clips_to_the_intersection(bool clipToBounds, float x, float y, float w, float h)
	{
		var rect = clipToBounds ? new Rect(-10, -10, 30, 30) : new Rect(5, 5, 20, 10);
		Laid(new RectangleGeometry { Rect = rect }, clipToBounds).Border.Visual.Clip.ShouldBe(new Rect(x, y, w, h));
	}

	[Test]
	public void No_clip_and_ClipToBounds_alone_are_unchanged()
	{
		Laid(null).Border.Visual.Clip.ShouldBeNull();
		Laid(null, clipToBounds: true).Border.Visual.Clip.ShouldBe(new Rect(0, 0, 40, 40));
	}

	[Test]
	public void A_negative_size_clips_everything_away()
	{
		var (_, root) = Laid(new RectangleGeometry { Rect = new Rect(5, 5, -20, 10) });
		root.Render().Commands.Length.ShouldBe(0);
	}

	/// <summary>
	/// A clip whose edges fall between pixel centres keeps exactly the pixels whose centres it covers, as a GPU's scissor
	/// does when the host rounds the clip by the same rule: a pixel whose centre lies on the near edge is in, one whose
	/// centre lies on the far edge is out. Rounded out instead, the oracle would draw a row or column the GPU does not.
	/// Both of the rasterizer's paths, the serial one and the banded one, hold to it.
	/// </summary>
	[Test]
	public void A_fractional_clip_keeps_the_pixels_whose_centres_it_covers()
	{
		// In surface pixels the clip is x 15.4..35.4 and y 15.6..25.6: centres from column 15 to 34 and row 16 to 25.
		var (_, root) = Laid(new RectangleGeometry { Rect = new Rect(5.4f, 5.6f, 20f, 10f) });
		var draw = root.Render();

		var serial = new SoftwareRasterizer(100, 100);
		serial.Render(draw);
		var banded = new SoftwareRasterizer(100, 100);
		banded.RenderParallel(draw, clearColor: Vector4.Zero);
		foreach (var (path, rgba) in new[] { ("serial", serial.ResolveToRgba8()), ("banded", banded.ResolveToRgba8()) })
			for (var y = 10; y < 50; y++)
				for (var x = 10; x < 50; x++)
				{
					var inside = x is >= 15 and <= 34 && y is >= 16 and <= 25;
					Alpha(rgba, x, y).ShouldBe(inside ? (byte)255 : (byte)0, $"{path}: pixel ({x},{y})");
				}
	}

	[Test]
	public void Hit_testing_honours_the_clip()
	{
		var (border, root) = Laid(new RectangleGeometry { Rect = new Rect(5, 5, 20, 10) });

		root.HitTest(new Vector2(20, 20)).ShouldBe(border);
		root.HitTest(new Vector2(12, 12)).ShouldBeNull();
		root.HitTest(new Vector2(45, 45)).ShouldBeNull();
	}

	[Test]
	public void Moving_the_geometry_reclips_at_the_next_composite_with_no_relayout()
	{
		var clip = new RectangleGeometry { Rect = new Rect(0, 0, 10, 10) };
		var (border, root) = Laid(clip);
		root.Render();

		clip.Rect = new Rect(0, 0, 30, 5);

		border.ArrangeDirty.ShouldBeFalse();
		border.MeasureDirty.ShouldBeFalse();
		border.Visual.IsDirty.ShouldBeTrue();
		border.Visual.Clip.ShouldBe(new Rect(0, 0, 30, 5));
		root.Render().Commands.Span[0].ClipRect.ShouldBe(new Rect(10, 10, 30, 5));
	}

	[Test]
	public void The_geometry_transform_moves_the_clip_with_no_relayout()
	{
		var move = new TranslateTransform { X = 5 };
		var (border, root) = Laid(new RectangleGeometry { Rect = new Rect(0, 0, 10, 10), Transform = move });
		border.Visual.Clip.ShouldBe(new Rect(5, 0, 10, 10));
		root.Render();

		move.Y = 4;

		border.ArrangeDirty.ShouldBeFalse();
		border.Visual.IsDirty.ShouldBeTrue();
		root.Render().Commands.Span[0].ClipRect.ShouldBe(new Rect(15, 14, 10, 10));
	}

	[Test]
	public void A_turned_clip_clips_to_the_bounds_of_the_turned_rectangle()
	{
		var clip = new RectangleGeometry { Rect = new Rect(0, 0, 10, 20), Transform = new RotateTransform { Angle = 90 } };
		Laid(clip).Border.Visual.Clip!.Value.ShouldBeNear(new Rect(-20, 0, 20, 10), 1e-4f);
	}

	[Test]
	public void A_storyboard_slides_the_clip_through_its_geometry_transform()
	{
		var slide = new TranslateTransform();
		var border = new Border { Width = 40f, Height = 40f, Clip = new RectangleGeometry { Rect = new Rect(0, 0, 10, 10), Transform = slide } };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = border;
		host.Tick(0, 0f, new Vector2(50f, 50f));

		host.Clock.Begin(new Storyboard
		{
			Children =
			{
				new DoubleAnimation
				{
					To = 30.0, Duration = TimeSpan.FromSeconds(1), Target = border,
					TargetProperty = new PropertyPath(UIElement.ClipProperty, Geometry.TransformProperty, TranslateTransform.XProperty),
				},
			},
		});
		host.Tick(0.5, 0f, new Vector2(50f, 50f));

		slide.X.ShouldBe(15f, 1e-4f);
		border.Visual.Clip.ShouldBe(new Rect(15, 0, 10, 10));
	}

	[Test]
	public void Setting_and_clearing_the_clip_applies_with_no_relayout()
	{
		var (border, _) = Laid(null);

		border.Clip = new RectangleGeometry { Rect = new Rect(1, 2, 3, 4) };
		border.Visual.Clip.ShouldBe(new Rect(1, 2, 3, 4));
		border.Clip = null;
		border.Visual.Clip.ShouldBeNull();
		border.ArrangeDirty.ShouldBeFalse();
	}

	[Test]
	public void The_element_listens_to_its_clip_only_while_in_a_live_tree()
	{
		var clip = new RectangleGeometry { Rect = new Rect(0, 0, 10, 10) };
		var border = new Border { Width = 40f, Height = 40f, Clip = clip };
		clip.ListenerCount.ShouldBe(0); // not in a tree yet

		var root = new UIRoot(new DrawListId(0)) { Content = border };
		clip.ListenerCount.ShouldBe(1);

		var other = new RectangleGeometry();
		border.Clip = other;
		(clip.ListenerCount, other.ListenerCount).ShouldBe((0, 1));

		root.Content = null;
		other.ListenerCount.ShouldBe(0);

		// Changed while out of the tree: it catches up when it joins one again.
		other.Rect = new Rect(2, 2, 5, 5);
		root.Content = border;
		other.ListenerCount.ShouldBe(1);
		border.Visual.Clip.ShouldBe(new Rect(2, 2, 5, 5));
	}

	[Test]
	public void A_clip_in_a_nested_subtree_registers_when_the_subtree_is_attached()
	{
		var clip = new RectangleGeometry { Rect = new Rect(0, 0, 10, 10) };
		var inner = new Border { Clip = clip };
		var panel = new StackPanel();
		panel.Children.Add(inner);
		var root = new UIRoot(new DrawListId(0));

		clip.ListenerCount.ShouldBe(0);
		root.Content = panel;
		clip.ListenerCount.ShouldBe(1);
		panel.Children.RemoveAt(0);
		clip.ListenerCount.ShouldBe(0);
	}

	private static byte Alpha(byte[] rgba, int x, int y) => rgba[(y * 100 + x) * 4 + 3];
}
