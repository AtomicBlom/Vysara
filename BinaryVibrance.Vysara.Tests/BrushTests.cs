using System.Numerics;
using System.Runtime.CompilerServices;
using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Media.Imaging;
using BinaryVibrance.Vysara.Rasterizer;
using BinaryVibrance.Vysara.Tests.Sample;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Brushes: what each kind paints, that a brush is shared and a change to it reaches every user in place with no
/// layout, that users register only while they are in a live tree (so a long-lived brush pins nothing removed), that a
/// storyboard on a shared brush animates every user as in WinUI, the image brush's limits, corner radii and the
/// background transition.
/// </summary>
public sealed class BrushTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(220, 40, 40);
	private static readonly HdrColor Blue = HdrColor.FromRgb(40, 40, 220);

	private static UIRoot Laid(UIElement content, float w = 200f, float h = 200f)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(w, h));
		return root;
	}

	[Test]
	public void A_solid_brush_paints_its_colour_with_its_opacity()
	{
		var border = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(Red) { Opacity = 0.5f } };
		var draw = Laid(border).Render();

		draw.Vertices.Span[0].Color.ShouldBe(Red.ToPremultiplied(0.5f));
		border.Visual.Dump().ShouldContain("fill=(0.863,0.157,0.157,1)*0.5");
	}

	[Test]
	public void A_change_to_a_shared_brush_repaints_every_user_in_place_with_no_layout()
	{
		var brush = new SolidColorBrush(Red);
		var first = new Border { Width = 10f, Height = 10f, Background = brush };
		var second = new TextBlock { Text = "Shared", Foreground = brush };
		var panel = new StackPanel();
		panel.Children.Add(first);
		panel.Children.Add(second);
		var root = Laid(panel);
		root.Render();
		var firstContent = first.Visual.Content;
		var secondContent = second.Visual.Content;

		brush.Color = Blue;

		first.Visual.IsDirty.ShouldBeTrue();
		second.Visual.IsDirty.ShouldBeTrue();
		first.MeasureDirty.ShouldBeFalse();
		first.ArrangeDirty.ShouldBeFalse();
		second.ArrangeDirty.ShouldBeFalse();
		first.Visual.Content.ShouldBeSameAs(firstContent);
		second.Visual.Content.ShouldBeSameAs(secondContent);
		first.ShownColor().ShouldBe(Blue);
		second.ShownColor().ShouldBe(Blue);
		root.Render().Vertices.Span[0].Color.ShouldBe(Blue.ToPremultiplied());
	}

	[Test]
	public void An_element_listens_to_its_brushes_only_while_it_is_in_a_live_tree()
	{
		var brush = new SolidColorBrush(Red);
		var other = new SolidColorBrush(Blue);
		var first = new Border { Background = brush, BorderBrush = brush }; // one brush in two properties: two uses
		var second = new Border { Background = brush };
		var panel = new StackPanel();
		panel.Children.Add(first);
		panel.Children.Add(second);
		brush.ListenerCount.ShouldBe(0); // nothing is live yet

		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		brush.ListenerCount.ShouldBe(3);

		panel.Children.RemoveAt(1);
		brush.ListenerCount.ShouldBe(2);
		panel.Children.Add(second);
		brush.ListenerCount.ShouldBe(3);

		second.Background = other; // moving to another brush moves the registration
		brush.ListenerCount.ShouldBe(2);
		other.ListenerCount.ShouldBe(1);

		root.Content = null;
		brush.ListenerCount.ShouldBe(0);
		other.ListenerCount.ShouldBe(0);
	}

	[Test]
	public void A_long_lived_brush_does_not_keep_a_removed_element_alive()
	{
		var brush = new SolidColorBrush(Red);
		var root = new UIRoot(new DrawListId(0));
		var removed = ShowAndRemove(root, brush);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		removed.IsAlive.ShouldBeFalse();
		brush.ListenerCount.ShouldBe(0);
	}

	// A border shown with `brush` and then removed from the tree, as a weak reference: the frame that made it is gone, so
	// only something still registered with the brush could keep it alive.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference ShowAndRemove(UIRoot root, Brush brush)
	{
		var border = new Border { Width = 10f, Height = 10f, Background = brush };
		root.Content = border;
		root.LayoutPass(new Size(50f, 50f));
		root.Render();
		brush.ListenerCount.ShouldBe(1);
		root.Content = null;
		return new WeakReference(border);
	}

	[Test]
	public void A_storyboard_on_a_shared_brush_animates_every_user_as_in_WinUI()
	{
		var view = new SharedBrushView();
		view.InitializeComponent();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0, 0f, new Vector2(200f, 200f));
		var accent = view.First.Background.ShouldBeOfType<SolidColorBrush>();
		var start = accent.Color;

		host.Clock.Begin(new Storyboard
		{
			Children =
			{
				new ColorAnimation
				{
					To = Red, Duration = TimeSpan.FromSeconds(1), Target = view.First,
					TargetProperty = new PropertyPath(Border.BackgroundProperty, SolidColorBrush.ColorProperty),
				},
			},
		});
		host.Tick(0.5, 0f, new Vector2(200f, 200f));

		var midway = Lerps.Color(start, Red, 0.5f);
		view.First.ShownColor().ShouldBe(midway);
		view.Second.ShownColor().ShouldBe(midway);  // the same resource brush
		view.Label.ShownColor().ShouldBe(midway);   // as a text foreground
		view.Styled.ShownColor().ShouldBe(midway);  // through a style's setter
		view.Styled.Background.ShouldBeSameAs(accent);
	}

	[Test]
	public void A_gradient_stop_change_reaches_the_brush_paint_in_place()
	{
		var view = new SharedBrushView();
		view.InitializeComponent();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0, 0f, new Vector2(200f, 200f));
		var content = view.Graded.Visual.Content.ShouldBeOfType<RectContent>();
		var paint = content.Fill.ShouldBeOfType<CompositionLinearGradientBrush>();
		var version = paint.Version;

		// The Pulse state's storyboard animates the named stop, which lives in the gradient resource.
		var pulse = VisualStateManager.GetVisualStateGroups(view.Graded)[0].States[0].Storyboard!;
		host.Clock.Begin(pulse);
		view.Graded.Visual.IsDirty = false;
		host.Clock.Advance(0.5);

		paint.Version.ShouldBeGreaterThan(version);
		view.Graded.Visual.IsDirty.ShouldBeTrue();
		view.Graded.Visual.Content.ShouldBeSameAs(content);
		paint.ColorStops[1].Color.ShouldBe(Lerps.Color(HdrColor.FromRgb(0, 0, 255), HdrColor.FromRgb(0, 255, 0), 0.5f));
		view.Graded.ArrangeDirty.ShouldBeFalse();
	}

	[Test]
	public void Gradient_stops_are_ordered_by_offset_and_a_shared_stop_reaches_every_brush()
	{
		var shared = new GradientStop { Color = Red, Offset = 0.5f };
		var a = new LinearGradientBrush { GradientStops = { new GradientStop { Color = Blue, Offset = 1f }, shared, new GradientStop { Color = Blue } } };
		var b = new LinearGradientBrush { GradientStops = { shared } };
		var paintA = (CompositionLinearGradientBrush)a.CompositionBrush;
		var paintB = (CompositionLinearGradientBrush)b.CompositionBrush;

		paintA.ColorStops.ToArray().Select(s => s.Offset).ToArray().ShouldBe(new[] { 0f, 0.5f, 1f });

		shared.Color = HdrColor.FromRgb(0, 255, 0);
		paintA.ColorStops[1].Color.ShouldBe(HdrColor.FromRgb(0, 255, 0));
		paintB.ColorStops[0].Color.ShouldBe(HdrColor.FromRgb(0, 255, 0));

		a.GradientStops.Remove(shared);
		shared.Offset = 0.25f;
		paintA.ColorStops.Length.ShouldBe(2);
		paintB.ColorStops[0].Offset.ShouldBe(0.25f);
	}

	[Test]
	public void The_gradient_brush_modes_reach_its_paint()
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = new Point(0.1f, 0.2f),
			EndPoint = new Point(0.9f, 0.8f),
			MappingMode = BrushMappingMode.Absolute,
			SpreadMethod = GradientSpreadMethod.Reflect,
			ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
			Opacity = 0.5f,
		};
		var paint = (CompositionLinearGradientBrush)brush.CompositionBrush;

		paint.StartPoint.ShouldBe(new Vector2(0.1f, 0.2f));
		paint.EndPoint.ShouldBe(new Vector2(0.9f, 0.8f));
		paint.MappingMode.ShouldBe(CompositionMappingMode.Absolute);
		paint.ExtendMode.ShouldBe(CompositionGradientExtendMode.Mirror);
		paint.InterpolationSpace.ShouldBe(CompositionColorSpace.RgbLinear);
		paint.Opacity.ShouldBe(0.5f);
		brush.SpreadMethod = GradientSpreadMethod.Repeat;
		paint.ExtendMode.ShouldBe(CompositionGradientExtendMode.Wrap);

		// WinUI's defaults: corner to corner, relative to the area painted, padded, interpolated in sRGB.
		var defaults = (CompositionLinearGradientBrush)new LinearGradientBrush().CompositionBrush;
		defaults.StartPoint.ShouldBe(Vector2.Zero);
		defaults.EndPoint.ShouldBe(Vector2.One);
		defaults.MappingMode.ShouldBe(CompositionMappingMode.Relative);
		defaults.ExtendMode.ShouldBe(CompositionGradientExtendMode.Clamp);
		defaults.InterpolationSpace.ShouldBe(CompositionColorSpace.Rgb);
	}

	[Test]
	public void An_image_brush_paints_a_plain_rect_cut_at_its_edges()
	{
		var image = BitmapSource.FromRgba8(4, 2, new byte[32]);
		var brush = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
		var border = new Border { Width = 40f, Height = 40f, Background = brush };
		var draw = Laid(border, 40f, 40f).Render();

		var command = draw.Commands.Span.ToArray().Single(c => (UIPrimKind)c.Tag == UIPrimKind.Image);
		draw.TextureSlots[command.TextureIndex].ShouldBeSameAs(image);
		// UniformToFill scales the 4×2 image to 80×40, centred; the box keeps its middle half, u from 0.25 to 0.75.
		var corners = draw.Vertices.Span.Slice(command.VertexOffset, 4).ToArray();
		corners[0].Position.ShouldBe(Vector2.Zero);
		corners[2].Position.ShouldBe(new Vector2(40f, 40f));
		corners[0].Uv.ShouldBe(new Vector2(0.25f, 0f));
		corners[2].Uv.ShouldBe(new Vector2(0.75f, 1f));
		border.Visual.Dump().ShouldContain("fill=image(BitmapSource 4x2 v0 straight srgb UniformToFill)");

		brush.AlignmentX = AlignmentX.Left;
		var shifted = border.AttachedRoot!.Render();
		var left = shifted.Commands.Span.ToArray().Single(c => (UIPrimKind)c.Tag == UIPrimKind.Image);
		shifted.Vertices.Span[left.VertexOffset].Uv.ShouldBe(new Vector2(0f, 0f));
		shifted.Vertices.Span[left.VertexOffset + 2].Uv.ShouldBe(new Vector2(0.5f, 1f));
	}

	[Test]
	public void An_image_brush_paints_rounded_corners_and_a_border_through_the_vector_prim()
	{
		var image = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);

		var rounded = new Border { Width = 40f, Height = 40f, CornerRadius = new CornerRadius(12f), Background = new ImageBrush { ImageSource = image } };
		var draw = Laid(rounded, 40f, 40f).Render();
		var command = draw.Commands.Span.ToArray().ShouldHaveSingleItem();
		((UIPrimKind)command.Tag, command.TextureIndex).ShouldBe((UIPrimKind.Vector, 1));
		var raster = Raster(draw, 40);
		raster.ReadLinear(20, 20).ShouldBe(new Vector4(1f, 0f, 0f, 1f));
		raster.ReadLinear(20, 0).W.ShouldBe(1f);
		raster.ReadLinear(0, 0).W.ShouldBe(0f); // the rounded corner is cut away

		// As a border, the image paints the ring and nothing inside it.
		var framed = new Border
		{
			Width = 40f,
			Height = 40f,
			CornerRadius = new CornerRadius(6f),
			BorderBrush = new ImageBrush { ImageSource = image },
			BorderThickness = new Thickness(4f),
		};
		raster = Raster(Laid(framed, 40f, 40f).Render(), 40);
		raster.ReadLinear(20, 3).W.ShouldBe(1f);
		raster.ReadLinear(20, 4).W.ShouldBe(0f);
		raster.ReadLinear(20, 20).W.ShouldBe(0f);

		// With square corners the image is still a plain quad, under a border of any paint.
		var squared = new Border
		{
			Width = 40f,
			Height = 40f,
			Background = new ImageBrush { ImageSource = image },
			BorderBrush = new SolidColorBrush(Blue),
			BorderThickness = new Thickness(2f),
		};
		Laid(squared, 40f, 40f).Render().Commands.Span.ToArray().Select(static c => (UIPrimKind)c.Tag).ShouldBe([UIPrimKind.Image, UIPrimKind.Border]);

		static SoftwareRasterizer Raster(DrawListData draw, int size)
		{
			var raster = new SoftwareRasterizer(size, size);
			raster.Clear(Vector4.Zero);
			raster.Render(draw);
			return raster;
		}
	}

	[Test]
	public void Image_brushes_on_a_round_avatar_and_a_border_from_markup_draw_as_vectors()
	{
		var view = new ImageCornersView();
		view.InitializeComponent();
		var image = BitmapSource.FromRgba8(1, 1, [0, 255, 0, 255]);
		view.Avatar.Background.ShouldBeOfType<ImageBrush>().ImageSource = image;
		view.Framed.BorderBrush.ShouldBeOfType<ImageBrush>().ImageSource = image;

		var draw = Laid(view.Root).Render();

		// One run of the Vector prim sampling the image, with the avatar above (y 0..32) and the frame below it.
		var command = draw.Commands.Span.ToArray().ShouldHaveSingleItem();
		((UIPrimKind)command.Tag, command.TextureIndex).ShouldBe((UIPrimKind.Vector, 1));
		var ys = draw.Vertices.Span.ToArray().Select(static v => v.Position.Y).ToArray();
		ys.Min().ShouldBe(0f, 0.6f); // the fringe reaches half a pixel past the edge
		ys.Max().ShouldBe(56f, 0.6f);
	}

	[Test]
	public void An_image_brush_on_a_text_block_is_refused()
	{
		var image = BitmapSource.FromRgba8(2, 2, new byte[16]);
		Should.Throw<ArgumentException>(() => new TextBlock { Text = "Hi", Foreground = new ImageBrush { ImageSource = image } })
			.Message.ShouldContain("cannot paint text");
	}

	[Test]
	public void Corners_of_their_own_radii_draw_through_the_vector_prim()
	{
		// Rounded at the top left and bottom right, square at the other two.
		var leaf = new Border { Width = 40f, Height = 40f, CornerRadius = new CornerRadius(16f, 0f, 8f, 0f), Background = new SolidColorBrush(Red) };
		leaf.CornerRadius.ShouldBe(new CornerRadius(16f, 0f, 8f, 0f));
		var draw = Laid(leaf, 40f, 40f).Render();
		((UIPrimKind)draw.Commands.Span.ToArray().ShouldHaveSingleItem().Tag).ShouldBe(UIPrimKind.Vector);
		leaf.Visual.Dump().ShouldContain("radii=(16,0,8,0)");
		var raster = Raster(draw);
		raster.ReadLinear(20, 20).W.ShouldBe(1f);
		raster.ReadLinear(0, 0).W.ShouldBe(0f); // cut away by the 16 radius
		raster.ReadLinear(3, 3).W.ShouldBe(0f);
		raster.ReadLinear(39, 0).W.ShouldBe(1f, 0.01f); // square
		raster.ReadLinear(39, 39).W.ShouldBe(0f); // cut away by the 8 radius
		raster.ReadLinear(34, 34).W.ShouldBe(1f, 0.01f);
		raster.ReadLinear(0, 39).W.ShouldBe(1f, 0.01f); // square

		// A border follows each corner, its inside rounded by the radius less the thickness: 12 at the top left.
		leaf.BorderBrush = new SolidColorBrush(Blue);
		leaf.BorderThickness = new Thickness(4f);
		leaf.AttachedRoot!.LayoutPass(new Size(40f, 40f));
		draw = leaf.AttachedRoot.Render();
		draw.Commands.Span.ToArray().ShouldAllBe(static c => (UIPrimKind)c.Tag == UIPrimKind.Vector);
		raster = Raster(draw);
		IsBlue(raster.ReadLinear(20, 1)).ShouldBeTrue(); // the top side
		IsBlue(raster.ReadLinear(6, 6)).ShouldBeTrue(); // 13.4 from the corner's centre: between 12 and 16
		IsBlue(raster.ReadLinear(8, 8)).ShouldBeFalse(); // 10.6 from it: inside the border
		raster.ReadLinear(8, 8).W.ShouldBe(1f, 0.01f);
		IsBlue(raster.ReadLinear(20, 20)).ShouldBeFalse();

		// One radius at every corner keeps the rounded-rect prim.
		leaf.CornerRadius = new CornerRadius(6f);
		leaf.AttachedRoot!.Render().Commands.Span.ToArray().Select(static c => (UIPrimKind)c.Tag).ShouldBe([UIPrimKind.RoundedRect, UIPrimKind.Border]);

		static SoftwareRasterizer Raster(DrawListData draw)
		{
			var raster = new SoftwareRasterizer(40, 40);
			raster.Clear(Vector4.Zero);
			raster.Render(draw);
			return raster;
		}

		static bool IsBlue(Vector4 color) => color.Z > 2f * color.X;
	}

	/// <summary>Radii a rect is too small for shrink together by one factor until each side holds its two corners, as CSS
	/// fits them; a negative or NaN radius is a square corner.</summary>
	[Test]
	public void Radii_too_large_for_a_rect_shrink_together()
	{
		new CompositionCornerRadius(30f, 10f, 0f, 0f).FitTo(new Vector2(20f, 40f)).ShouldBe(new CompositionCornerRadius(15f, 5f, 0f, 0f));
		new CompositionCornerRadius(40f).FitTo(new Vector2(40f, 80f)).ShouldBe(new CompositionCornerRadius(20f)); // half the shorter side
		new CompositionCornerRadius(4f, -2f, float.NaN, 6f).FitTo(new Vector2(40f, 40f)).ShouldBe(new CompositionCornerRadius(4f, 0f, 0f, 6f));
		new CompositionCornerRadius(8f, 2f, 0f, 5f).FitTo(new Vector2(40f, 40f)).ShouldBe(new CompositionCornerRadius(8f, 2f, 0f, 5f));
	}

	[Test]
	public void One_radius_too_large_for_a_rect_draws_a_pill()
	{
		// WinUI's pill idiom: a radius far past half the height rounds each end into a half circle of half the height.
		var pill = new Border { Width = 100f, Height = 32f, CornerRadius = new CornerRadius(999f), Background = new SolidColorBrush(Red) };
		((UIPrimKind)Laid(pill, 100f, 32f).Render().Commands.Span.ToArray().ShouldHaveSingleItem().Tag).ShouldBe(UIPrimKind.RoundedRect);

		var raster = new SoftwareRasterizer(100, 32);
		raster.Clear(Vector4.Zero);
		raster.Render(pill.AttachedRoot!.Render());
		raster.ReadLinear(50, 16).W.ShouldBe(1f, 0.01f);
		raster.ReadLinear(1, 16).W.ShouldBe(1f, 0.01f); // the ends reach the sides
		raster.ReadLinear(98, 16).W.ShouldBe(1f, 0.01f);
		raster.ReadLinear(1, 1).W.ShouldBe(0f); // and are round
		raster.ReadLinear(98, 30).W.ShouldBe(0f);

		// A radius that isn't a number draws square corners.
		pill.CornerRadius = new CornerRadius(float.NaN);
		((UIPrimKind)pill.AttachedRoot.Render().Commands.Span.ToArray().ShouldHaveSingleItem().Tag).ShouldBe(UIPrimKind.Fill);
	}

	[Test]
	public void A_brush_transition_fades_between_solid_backgrounds_on_the_host_clock()
	{
		var border = new Border
		{
			Width = 10f,
			Height = 10f,
			Background = new SolidColorBrush(Red),
			BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromSeconds(1) },
		};
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = border;
		host.Tick(0, 0f, new Vector2(50f, 50f));

		var blue = new SolidColorBrush(Blue);
		border.Background = blue;
		border.ShownColor().ShouldBe(Red); // the fade starts from the colour on screen
		host.Tick(0.5, 0f, new Vector2(50f, 50f));
		border.ShownColor().ShouldBe(Lerps.Color(Red, Blue, 0.5f));
		border.ArrangeDirty.ShouldBeFalse();

		// A change mid-fade starts the next fade from the colour on screen.
		border.Background = new SolidColorBrush(Red);
		host.Tick(0.5, 0f, new Vector2(50f, 50f));
		border.ShownColor().ShouldBe(Lerps.Color(Lerps.Color(Red, Blue, 0.5f), Red, 0.5f));
		host.Tick(0.5, 0f, new Vector2(50f, 50f));
		border.Visual.Content.ShouldBeOfType<RectContent>().Fill.ShouldBeSameAs(border.Background!.CompositionBrush); // the brush's own paint again

		// To or from no brush fades the colour out or in; any other change shows at once.
		border.Background = null;
		host.Tick(0.5, 0f, new Vector2(50f, 50f));
		border.ShownColor().ShouldBe(Red with { A = 0.5f });
		host.Tick(0.5, 0f, new Vector2(50f, 50f));
		border.Visual.Content.ShouldBeNull();
		var gradient = new LinearGradientBrush();
		border.Background = gradient;
		border.Visual.Content.ShouldBeOfType<RectContent>().Fill.ShouldBeSameAs(gradient.CompositionBrush);
	}

	[Test]
	public void Without_a_live_host_a_brush_transition_shows_the_new_brush_at_once()
	{
		var border = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(Red), BackgroundTransition = new BrushTransition() };
		Laid(border);

		border.Background = new SolidColorBrush(Blue);
		border.ShownColor().ShouldBe(Blue);
	}
}
