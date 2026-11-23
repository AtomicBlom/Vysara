using System.Numerics;
using Vysara.Animation;
using Vysara.Composition;
using Vysara.Controls;
using Vysara.Drawing;
using Vysara.Media;
using Vysara.Media.Imaging;
using Vysara.Rasterizer;
using Vysara.Shapes;

namespace Vysara.Tests;

/// <summary>
/// A collapsed element contributes nothing to the frame, its own content included: a leaf (text, a shape, an image)
/// collapsed after it was drawn, before its first frame, under an ancestor whose opacity is animating, or while a
/// property it draws with changes, leaves no command in the draw list and no pixel. Shown again it draws where layout
/// puts it now, with the content it kept, and once it is collapsed a frame with nothing else to do recomposites nothing
/// and allocates nothing. Pixels through the CPU oracle, on black.
/// </summary>
public sealed class CollapsedTests
{
	public enum Leaf { Text, Rectangle, Polyline, Image }

	private const int Side = 100;
	private static readonly HdrColor Red = HdrColor.FromRgb(255, 0, 0);
	private static readonly HdrColor Blue = HdrColor.FromRgb(0, 0, 255);

	// A red leaf about 40×20 DIPs.
	private static FrameworkElement Make(Leaf leaf) => leaf switch
	{
		Leaf.Text => new TextBlock { Text = "MMM", FontSize = 20f, Foreground = new SolidColorBrush(Red) },
		Leaf.Rectangle => new Rectangle { Width = 40f, Height = 20f, Fill = new SolidColorBrush(Red) },
		Leaf.Polyline => new Polyline
		{
			Points = { new Point(0, 0), new Point(40, 0), new Point(40, 20) },
			Stroke = new SolidColorBrush(Red),
			StrokeThickness = 4f,
		},
		_ => new Image { Source = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]), Width = 40f, Height = 20f, Stretch = Stretch.Fill },
	};

	// Change something the leaf draws with that does not move its layout, so only its content is brought up to date.
	private static void Repaint(FrameworkElement leaf)
	{
		switch (leaf)
		{
			case TextBlock text:
				text.Foreground = new SolidColorBrush(Blue);
				break;
			case Rectangle rectangle:
				rectangle.Fill = new SolidColorBrush(Blue);
				break;
			case Polyline polyline:
				polyline.Stroke = new SolidColorBrush(Blue);
				break;
			case Image image:
				image.Stretch = Stretch.Uniform;
				break;
		}
	}

	private static Canvas At(FrameworkElement leaf, float left, float top)
	{
		Canvas.SetLeft(leaf, left);
		Canvas.SetTop(leaf, top);
		var canvas = new Canvas();
		canvas.Children.Add(leaf);
		return canvas;
	}

	private static DrawListData Frame(UIRoot root)
	{
		root.LayoutPass(new Size(Side, Side));
		return root.Render();
	}

	// The commands that draw something: every one but a group layer's markers.
	private static int Drawn(DrawListData draw)
	{
		var count = 0;
		foreach (var command in draw.Commands.Span)
		{
			if ((UIPrimKind)command.Tag is not (UIPrimKind.BeginLayer or UIPrimKind.EndLayer))
				count++;
		}
		return count;
	}

	private static SoftwareRasterizer Raster(DrawListData draw)
	{
		var raster = new SoftwareRasterizer(Side, Side);
		raster.Clear(new Vector4(0f, 0f, 0f, 1f));
		raster.Render(draw);
		return raster;
	}

	// How many pixels of rows [top, bottom) are not black.
	private static int Lit(SoftwareRasterizer raster, int top = 0, int bottom = Side)
	{
		var lit = 0;
		for (var y = top; y < bottom; y++)
		{
			for (var x = 0; x < Side; x++)
			{
				var p = raster.ReadLinear(x, y);
				if (MathF.Max(p.X, MathF.Max(p.Y, p.Z)) > 0.01f)
					lit++;
			}
		}
		return lit;
	}

	private static void ShouldDrawNothing(DrawListData draw, FrameworkElement leaf)
	{
		Drawn(draw).ShouldBe(0, draw.Dump());
		Lit(Raster(draw)).ShouldBe(0);
		leaf.Visual.Content.ShouldBeNull();
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	public void A_leaf_collapsed_after_it_was_drawn_draws_nothing(Leaf kind)
	{
		var leaf = Make(kind);
		var root = new UIRoot(new DrawListId(0)) { Content = At(leaf, 30f, 10f) };

		var drawn = Frame(root);
		Drawn(drawn).ShouldBeGreaterThan(0);
		Lit(Raster(drawn)).ShouldBeGreaterThan(0);

		leaf.Visibility = Visibility.Collapsed;
		ShouldDrawNothing(Frame(root), leaf);
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	public void A_leaf_collapsed_before_its_first_frame_draws_nothing(Leaf kind)
	{
		var leaf = Make(kind);
		leaf.Visibility = Visibility.Collapsed;
		var root = new UIRoot(new DrawListId(0)) { Content = At(leaf, 30f, 10f) };

		ShouldDrawNothing(Frame(root), leaf);
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	public void A_leaf_shown_again_draws_where_it_is_laid_out_now(Leaf kind)
	{
		var leaf = Make(kind);
		var root = new UIRoot(new DrawListId(0)) { Content = At(leaf, 30f, 10f) };
		Frame(root);
		var content = leaf.Visual.Content;

		// Collapsed, then moved to the lower half while collapsed: drawn nowhere, at the old place or the new.
		leaf.Visibility = Visibility.Collapsed;
		Frame(root);
		Canvas.SetTop(leaf, 60f);
		ShouldDrawNothing(Frame(root), leaf);

		leaf.Visibility = Visibility.Visible;
		var shown = Raster(Frame(root));
		leaf.Visual.Content.ShouldBeSameAs(content); // the content it kept, brought up to date

		// Exactly what the same leaf draws there having never been collapsed.
		var fresh = Make(kind);
		var expected = Raster(Frame(new UIRoot(new DrawListId(0)) { Content = At(fresh, 30f, 60f) }));
		Lit(shown, 0, 50).ShouldBe(0);
		Lit(shown, 50).ShouldBeGreaterThan(0);
		shown.ResolveToRgba8().ShouldBe(expected.ResolveToRgba8());
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	public void Repainting_a_collapsed_leaf_does_not_bring_it_back(Leaf kind)
	{
		var leaf = Make(kind);
		var root = new UIRoot(new DrawListId(0)) { Content = At(leaf, 30f, 10f) };
		Frame(root);
		leaf.Visibility = Visibility.Collapsed;
		Frame(root);

		Repaint(leaf);
		ShouldDrawNothing(Frame(root), leaf);

		// Shown again, it draws as it was changed while collapsed.
		leaf.Visibility = Visibility.Visible;
		var shown = Raster(Frame(root));
		var expected = Make(kind);
		Repaint(expected);
		shown.ResolveToRgba8().ShouldBe(Raster(Frame(new UIRoot(new DrawListId(0)) { Content = At(expected, 30f, 10f) })).ResolveToRgba8());
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	[NotInParallel(TestIsolation.HotReload)]
	public void A_leaf_collapsed_under_an_ancestor_whose_opacity_animates_draws_nothing(Leaf kind)
	{
		var leaf = Make(kind);
		var panel = new StackPanel();
		panel.Children.Add(leaf);
		var fading = new Border { Child = panel };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = fading;
		var storyboard = new Storyboard();
		storyboard.Children.Add(new DoubleAnimation
		{
			From = 1.0,
			To = 0.2,
			Duration = TimeSpan.FromSeconds(1),
			Target = fading,
			TargetProperty = UIElement.OpacityProperty,
		});
		host.Clock.Begin(storyboard);

		host.Tick(0.1, 0f, new Vector2(Side, Side));
		host.Render();
		var faded = host.DrawLists[0].Data;
		Drawn(faded).ShouldBeGreaterThan(0);
		Lit(Raster(faded)).ShouldBeGreaterThan(0);

		leaf.Visibility = Visibility.Collapsed;
		var revision = faded.Revision;
		for (var i = 0; i < 6; i++)
		{
			host.Tick(0.1, 0f, new Vector2(Side, Side));
			host.Render();
			var draw = host.DrawLists[0].Data;
			draw.Revision.ShouldBeGreaterThan(revision); // the fade recomposites every frame
			revision = draw.Revision;
			ShouldDrawNothing(draw, leaf);
		}
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	[NotInParallel(TestIsolation.HotReload)]
	public void Frames_after_a_collapse_recomposite_nothing_and_allocate_nothing(Leaf kind)
	{
		var leaf = Make(kind);
		var compositor = new DrawListCompositor();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default, compositor: compositor).Root.Content = At(leaf, 30f, 10f);
		Tick();

		leaf.Visibility = Visibility.Collapsed;
		Tick();
		var composites = compositor.CompositeCount;
		var revision = host.DrawLists[0].Data.Revision;

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 64; i++)
				Tick();
		});

		compositor.CompositeCount.ShouldBe(composites);
		host.DrawLists[0].Data.Revision.ShouldBe(revision);
		ShouldDrawNothing(host.DrawLists[0].Data, leaf);

		void Tick()
		{
			host.Tick(0.016, 0f, new Vector2(Side, Side));
			host.Render();
		}
	}

	[Test]
	[Arguments(Leaf.Text)]
	[Arguments(Leaf.Rectangle)]
	[Arguments(Leaf.Polyline)]
	[Arguments(Leaf.Image)]
	public void A_leaf_collapsed_in_a_panel_that_does_not_arrange_it_draws_nothing(Leaf kind)
	{
		var leaf = Make(kind);
		var panel = new SkipsCollapsed();
		panel.Children.Add(leaf);
		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		Drawn(Frame(root)).ShouldBeGreaterThan(0);

		leaf.Visibility = Visibility.Collapsed;
		ShouldDrawNothing(Frame(root), leaf);

		leaf.Visibility = Visibility.Visible;
		Lit(Raster(Frame(root))).ShouldBeGreaterThan(0);
	}

	[Test]
	public void A_collapsed_panel_draws_none_of_its_children()
	{
		var panel = new StackPanel();
		var leaves = new[] { Make(Leaf.Text), Make(Leaf.Rectangle), Make(Leaf.Polyline), Make(Leaf.Image) };
		foreach (var leaf in leaves)
			panel.Children.Add(leaf);
		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		Drawn(Frame(root)).ShouldBeGreaterThan(0);

		panel.Visibility = Visibility.Collapsed;
		var draw = Frame(root);

		Drawn(draw).ShouldBe(0, draw.Dump());
		Lit(Raster(draw)).ShouldBe(0);
	}

	// A panel that leaves its collapsed children unarranged, as a panel may, and places the rest at (30, 10).
	private sealed class SkipsCollapsed : Panel
	{
		protected override Size MeasureOverride(Size available)
		{
			foreach (var child in Children.Items)
				child.Measure(Size.Infinite);
			return Size.Zero;
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			foreach (var child in Children.Items)
			{
				if (child.Visibility == Visibility.Visible)
					child.Arrange(new Rect(30f, 10f, child.DesiredSize.Width, child.DesiredSize.Height));
			}
			return finalSize;
		}
	}
}
