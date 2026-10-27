using System.Numerics;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Extensions;
using SUIsei.Media;
using SUIsei.Media.Imaging;
using SUIsei.Rasterizer;
using SUIsei.Shapes;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// Image sources: <see cref="BitmapSource"/>, <see cref="WriteableBitmap"/> and <see cref="HostImageSource"/>, each
/// declaring how its texels read (alpha mode and colour space), which the rasterizer honours exactly as a GPU does
/// through either prim that paints with an image; a rewritten bitmap reaching the host through its version; and the
/// host's release list, which names each source its surfaces stop drawing, or that is disposed while drawn.
/// </summary>
public sealed class ImageSourceTests
{
	public enum Prim
	{
		Image,
		Vector,
	}

	// One texel's colour bytes, decoded as its colour space says: the sRGB transfer function, written out here rather
	// than taken from the rasterizer, so the oracle is checked against the definition.
	private static float Decode(byte value, TextureColorSpace colorSpace)
	{
		var c = value / 255.0;
		if (colorSpace == TextureColorSpace.Linear)
			return (float)c;
		return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
	}

	// A 1×1 image of one texel.
	private static BitmapSource Texel(byte r, byte g, byte b, byte a, TextureAlphaMode alphaMode, TextureColorSpace colorSpace) =>
		BitmapSource.FromRgba8(1, 1, [r, g, b, a], alphaMode, colorSpace);

	// An element filling a 16×16 box with `source`: an Image (the Image prim) or an ellipse filled by an ImageBrush (the
	// Vector prim, sampled through the same slot).
	private static UIElement Painted(Prim prim, ImageSource source) => prim == Prim.Image
		? new Image { Source = source, Width = 16f, Height = 16f, Stretch = Stretch.Fill }
		: new Ellipse { Width = 16f, Height = 16f, Fill = new ImageBrush { ImageSource = source } };

	private static (UIHost Host, UIView View) Hosted(UIElement content)
	{
		// At the surface's top left, where the pixel probes look, rather than centred in it.
		if (content is FrameworkElement element)
		{
			element.HorizontalAlignment = HorizontalAlignment.Left;
			element.VerticalAlignment = VerticalAlignment.Top;
		}
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		view.Root.Content = content;
		return (host, view);
	}

	private static DrawListData Frame(UIHost host, float width = 64f, float height = 64f)
	{
		host.Tick(0.016, 0f, new Vector2(width, height));
		host.Render();
		return host.DrawLists[0].Data;
	}

	private static SoftwareRasterizer Rasterized(DrawListData draw, int width = 64, int height = 64)
	{
		var raster = new SoftwareRasterizer(width, height);
		raster.Clear(Vector4.Zero);
		raster.Render(draw);
		return raster;
	}

	private static void ShouldBeClose(Vector4 actual, Vector4 expected, float tolerance = 1e-5f)
	{
		Vector4.Abs(actual - expected).ShouldSatisfyAllConditions(
			d => d.X.ShouldBeLessThan(tolerance, $"red: {actual} vs {expected}"),
			d => d.Y.ShouldBeLessThan(tolerance, $"green: {actual} vs {expected}"),
			d => d.Z.ShouldBeLessThan(tolerance, $"blue: {actual} vs {expected}"),
			d => d.W.ShouldBeLessThan(tolerance, $"alpha: {actual} vs {expected}"));
	}

	[Test]
	[Arguments(Prim.Image, TextureAlphaMode.Straight, TextureColorSpace.Srgb)]
	[Arguments(Prim.Image, TextureAlphaMode.Straight, TextureColorSpace.Linear)]
	[Arguments(Prim.Image, TextureAlphaMode.Premultiplied, TextureColorSpace.Srgb)]
	[Arguments(Prim.Image, TextureAlphaMode.Premultiplied, TextureColorSpace.Linear)]
	[Arguments(Prim.Vector, TextureAlphaMode.Straight, TextureColorSpace.Srgb)]
	[Arguments(Prim.Vector, TextureAlphaMode.Straight, TextureColorSpace.Linear)]
	[Arguments(Prim.Vector, TextureAlphaMode.Premultiplied, TextureColorSpace.Srgb)]
	[Arguments(Prim.Vector, TextureAlphaMode.Premultiplied, TextureColorSpace.Linear)]
	public void An_image_blends_over_what_is_behind_it_as_its_source_declares(Prim prim, TextureAlphaMode alphaMode, TextureColorSpace colorSpace)
	{
		// Half-transparent texels over opaque white. The colour decodes per its colour space; straight colour is then
		// multiplied by alpha, premultiplied colour already is; the result blends over the white premultiplied.
		const byte R = 100, G = 40, B = 0, A = 128;
		var grid = new Grid { Width = 16f, Height = 16f, Background = new SolidColorBrush(HdrColor.FromRgb(255, 255, 255)) };
		grid.Children.Add(Painted(prim, Texel(R, G, B, A, alphaMode, colorSpace)));
		var (host, _) = Hosted(grid);

		var pixel = Rasterized(Frame(host)).ReadLinear(8, 8);

		var a = A / 255f;
		var scale = alphaMode == TextureAlphaMode.Straight ? a : 1f;
		var image = new Vector3(Decode(R, colorSpace), Decode(G, colorSpace), Decode(B, colorSpace)) * scale;
		var expected = image + Vector3.One * (1f - a);
		ShouldBeClose(pixel, new Vector4(expected, 1f));
	}

	[Test]
	public void Images_default_to_straight_srgb_and_atlas_pages_to_raw()
	{
		BitmapSource.FromRgba8(1, 1, [0, 0, 0, 0]).ShouldSatisfyAllConditions(
			s => s.AlphaMode.ShouldBe(TextureAlphaMode.Straight),
			s => s.ColorSpace.ShouldBe(TextureColorSpace.Srgb));
		new WriteableBitmap(2, 2).ShouldSatisfyAllConditions(
			s => s.AlphaMode.ShouldBe(TextureAlphaMode.Straight),
			s => s.ColorSpace.ShouldBe(TextureColorSpace.Srgb),
			s => s.PixelBuffer.Length.ShouldBe(16));
		new HostImageSource("t", 4, 4).ShouldSatisfyAllConditions(
			s => s.AlphaMode.ShouldBe(TextureAlphaMode.Premultiplied),
			s => s.ColorSpace.ShouldBe(TextureColorSpace.Linear),
			s => s.Pixels.IsEmpty.ShouldBeTrue());
		// Distances and the white texel are read as stored, so lists that draw only text and fills sample as they did.
		FontAtlas.Default.Image.AlphaMode.ShouldBe(TextureAlphaMode.Premultiplied);
		FontAtlas.Default.Image.ColorSpace.ShouldBe(TextureColorSpace.Linear);
		TextureSource.White.ColorSpace.ShouldBe(TextureColorSpace.Linear);
	}

	[Test]
	public void Straight_texels_are_premultiplied_after_filtering_as_a_gpu_does()
	{
		// Opaque white beside transparent black, stretched over four pixels. Pixel 1's centre samples a quarter of the way
		// from the white texel to the transparent one: the filtered sample is 0.75 in every channel, then premultiplied to
		// 0.5625. Premultiplying before filtering would give 0.75; that darker fringe is the documented cost of straight
		// alpha, on the GPU and here alike.
		var source = BitmapSource.FromRgba8(2, 1, [255, 255, 255, 255, 0, 0, 0, 0]);
		var (host, _) = Hosted(new Image { Source = source, Width = 4f, Height = 1f, Stretch = Stretch.Fill });

		var raster = Rasterized(Frame(host));

		ShouldBeClose(raster.ReadLinear(1, 0), new Vector4(0.5625f, 0.5625f, 0.5625f, 0.75f));
	}

	[Test]
	public void Invalidate_moves_the_version_and_the_texture_revision_without_a_recomposite()
	{
		var bitmap = new WriteableBitmap(2, 2);
		var canvas = new Canvas { Width = 64f, Height = 64f };
		var image = new Image { Source = bitmap, Width = 8f, Height = 8f, Stretch = Stretch.Fill };
		var dot = new Ellipse { Width = 16f, Height = 16f, Fill = new ImageBrush { ImageSource = bitmap } };
		Canvas.SetLeft(dot, 32f);
		Canvas.SetTop(dot, 32f);
		canvas.Children.Add(image);
		canvas.Children.Add(dot);
		var (host, _) = Hosted(canvas);
		var first = Frame(host);
		var (revision, textureRevision) = (first.Revision, first.TextureRevision);
		var raster = new SoftwareRasterizer(64, 64);
		raster.Render(first);
		raster.ReadLinear(4, 4).ShouldBe(Vector4.Zero); // a new bitmap is transparent

		Fill(bitmap, 255, 0, 0);
		bitmap.Invalidate();
		bitmap.Version.ShouldBe(1u);
		var rewritten = Frame(host);

		rewritten.Revision.ShouldBe(revision);              // same geometry: nothing recomposited
		rewritten.TextureRevision.ShouldBe(textureRevision + 1);
		// The damage is where the bitmap is drawn: from the image at the origin to the ellipse, whose fringe reaches half a
		// pixel past its 32..48 box.
		rewritten.Damage.X.ShouldBe(0f);
		rewritten.Damage.Y.ShouldBe(0f);
		rewritten.Damage.Right.ShouldBeInRange(48f, 49f);
		rewritten.Damage.Bottom.ShouldBeInRange(48f, 49f);
		rewritten.Dump().ShouldContain("WriteableBitmap 2x2 v1 straight srgb");
		raster.Render(rewritten);
		raster.ReadLinear(4, 4).ShouldBe(new Vector4(1f, 0f, 0f, 1f));
		ShouldBeClose(raster.ReadLinear(40, 40), new Vector4(1f, 0f, 0f, 1f));

		var steady = Frame(host);
		steady.TextureRevision.ShouldBe(textureRevision + 1);

		// The rasterizer copies the next version over the texels it kept, as a host replaces its texture.
		var cached = raster.CachedTextureCount;
		Fill(bitmap, 0, 0, 255);
		bitmap.Invalidate();
		raster.Render(Frame(host));
		raster.ReadLinear(4, 4).ShouldBe(new Vector4(0f, 0f, 1f, 1f));
		raster.CachedTextureCount.ShouldBe(cached);
	}

	private static void Fill(WriteableBitmap bitmap, byte r, byte g, byte b)
	{
		var pixels = bitmap.PixelBuffer.Span;
		for (var i = 0; i < pixels.Length; i += 4)
		{
			pixels[i] = r;
			pixels[i + 1] = g;
			pixels[i + 2] = b;
			pixels[i + 3] = 255;
		}
	}

	[Test]
	public void A_disposed_bitmap_cannot_be_invalidated()
	{
		var bitmap = new WriteableBitmap(1, 1);
		bitmap.Dispose();

		Should.Throw<ObjectDisposedException>(bitmap.Invalidate);
	}

	[Test]
	public void A_bitmap_needs_as_many_bytes_as_its_texels()
	{
		Should.Throw<ArgumentException>(() => BitmapSource.FromRgba8(2, 2, new byte[15]));
		Should.Throw<ArgumentNullException>(() => BitmapSource.FromRgba8(2, 2, null!));
		Should.Throw<ArgumentOutOfRangeException>(() => new WriteableBitmap(0, 4));
		Should.Throw<ArgumentOutOfRangeException>(() => new WriteableBitmap(65536, 65536)); // more bytes than an array holds
		Should.Throw<ArgumentOutOfRangeException>(() => new HostImageSource("t", 4, -1));
	}

	[Test]
	public void A_texel_change_under_a_group_layer_damages_the_whole_surface()
	{
		// A layer's composite can carry a change past what was drawn in it (an effect's blur), so damage covers the
		// surface, on a clean frame as on a recomposite.
		var bitmap = new WriteableBitmap(2, 2);
		var group = new StackPanel { Opacity = 0.5f };
		group.Children.Add(new Image { Source = bitmap, Width = 8f, Height = 8f });
		group.Children.Add(new Border { Width = 8f, Height = 8f, Background = new SolidColorBrush(HdrColor.FromRgb(9, 9, 9)) });
		var (host, _) = Hosted(group);
		Frame(host);

		bitmap.Invalidate();
		var draw = Frame(host);

		draw.Damage.ShouldBe(new Rect(0f, 0f, 64f, 64f));
	}

	[Test]
	public void A_host_image_lays_out_by_its_size_and_the_rasterizer_skips_and_counts_what_samples_it()
	{
		var minimap = new HostImageSource("minimap", 32, 16);
		var image = new Image { Source = minimap, Stretch = Stretch.None };
		var panel = new StackPanel();
		panel.Children.Add(image);
		panel.Children.Add(new Rectangle { Width = 32f, Height = 16f, Fill = new ImageBrush { ImageSource = minimap } });
		var (host, _) = Hosted(panel);

		var draw = Frame(host);

		image.DesiredSize.ShouldBe(new Size(32f, 16f));
		draw.TextureSlots.ShouldContain(minimap);
		draw.Dump().ShouldContain("HostImageSource \"minimap\" 32x16 v0 premultiplied linear");
		var raster = Rasterized(draw);
		raster.SkippedCommands.ShouldBe(2); // the image quad and the rectangle's vector fill
		raster.ReadLinear(8, 8).ShouldBe(Vector4.Zero);
		raster.ReadLinear(8, 24).ShouldBe(Vector4.Zero);
		raster.RenderParallel(draw, Vector4.Zero);
		raster.SkippedCommands.ShouldBe(2); // counted once, however many bands replay them
	}

	[Test]
	public void A_source_is_released_after_the_render_that_stops_drawing_it()
	{
		var a = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);
		var b = BitmapSource.FromRgba8(1, 1, [0, 255, 0, 255]);
		var c = BitmapSource.FromRgba8(1, 1, [0, 0, 255, 255]);
		var firstA = new Image { Source = a, Width = 4f, Height = 4f };
		var secondA = new Image { Source = a, Width = 4f, Height = 4f };
		var showsB = new Image { Source = b, Width = 4f, Height = 4f };
		var panel = new StackPanel();
		panel.Children.Add(firstA);
		panel.Children.Add(showsB);
		panel.Children.Add(secondA);
		var (host, _) = Hosted(panel);

		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty();
		host.DumpTextures().ShouldStartWith("Textures held (2):\n");

		// Another element still draws a: kept.
		panel.Children.RemoveAt(0); // firstA
		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty();

		// Detached: the last element drawing a is gone.
		panel.Children.RemoveAt(1); // secondA, after showsB
		Frame(host);
		host.ReleasedTextures.ShouldBe([a]);
		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty(); // reported once

		// Replaced: the image now draws c.
		showsB.Source = c;
		Frame(host);
		host.ReleasedTextures.ShouldBe([b]);
		host.DrawLists[0].Data.TextureSlots.ShouldNotContain(b);
	}

	[Test]
	public void A_source_several_surfaces_draw_is_released_when_the_last_lets_it_go()
	{
		var shared = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);
		var host = new UIHost();
		var one = host.CreateRoot(UILayer.Default);
		var two = host.CreateRoot(UILayer.Default);
		var inOne = new Image { Source = shared, Width = 4f, Height = 4f };
		one.Root.Content = inOne;
		two.Root.Content = new Image { Source = shared, Width = 4f, Height = 4f };
		Frame(host);
		host.DumpTextures().ShouldContain("surfaces=2");

		two.Root.Content = new Border { Width = 4f, Height = 4f };
		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty(); // the first surface still draws it
		host.DumpTextures().ShouldContain("surfaces=1");

		// Moved from the first surface to the second within a frame: the first lets it go and the second takes it up in
		// the same render, so it is kept rather than released and uploaded again.
		one.Root.Content = null;
		two.Root.Content = inOne;
		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty();
		host.DumpTextures().ShouldContain("surfaces=1");

		host.RemoveRoot(two);
		Frame(host);
		host.ReleasedTextures.ShouldBe([shared]);
		host.DumpTextures().ShouldStartWith("Textures held (0):\n");
	}

	[Test]
	public void Disposing_a_drawn_image_redraws_without_it_and_reports_it_once()
	{
		var bitmap = new WriteableBitmap(1, 1);
		Fill(bitmap, 255, 0, 0);
		var image = new Image { Source = bitmap, Width = 4f, Height = 4f };
		var dot = new Ellipse { Width = 8f, Height = 8f, Fill = new ImageBrush { ImageSource = bitmap } };
		var panel = new StackPanel();
		panel.Children.Add(image);
		panel.Children.Add(dot);
		var (host, _) = Hosted(panel);
		var drawn = Frame(host);
		drawn.TextureSlots.ShouldContain(bitmap);

		bitmap.Dispose();
		// The list the host already has was made before the dispose and still names the bitmap: it is drawn as it was,
		// from the texture the host keeps until its next render releases it.
		var before = Rasterized(drawn);
		before.SkippedCommands.ShouldBe(0);
		before.ReadLinear(2, 2).ShouldBe(new Vector4(1f, 0f, 0f, 1f));
		var redrawn = Frame(host);

		bitmap.IsDisposed.ShouldBeTrue();
		redrawn.TextureSlots.ShouldNotContain(bitmap);
		redrawn.Commands.ToArray().ShouldNotContain(cmd => (UIPrimKind)cmd.Tag == UIPrimKind.Image || (UIPrimKind)cmd.Tag == UIPrimKind.Vector);
		host.ReleasedTextures.ShouldBe([bitmap]);
		image.Visual.Dump().ShouldContain("image(WriteableBitmap 1x1 v0 straight srgb disposed Uniform)");

		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty();
		bitmap.Dispose(); // twice does nothing
		Frame(host);
		host.ReleasedTextures.ShouldBeEmpty();
	}

	[Test]
	public async Task An_image_disposed_on_another_thread_is_released_at_the_next_render()
	{
		var bitmap = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);
		var (host, _) = Hosted(new Image { Source = bitmap, Width = 4f, Height = 4f });
		Frame(host);

		await Task.Run(bitmap.Dispose);
		var redrawn = Frame(host);

		redrawn.TextureSlots.ShouldNotContain(bitmap);
		host.ReleasedTextures.ShouldBe([bitmap]);
	}

	[Test]
	public void A_long_lived_source_keeps_no_host_alive()
	{
		// A static or cached source (the default font atlas, an image shared across windows) outlives the hosts that drew
		// it, so it must hold no reference that keeps them, or their element trees, alive.
		var shared = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);
		var host = DrawnAndDropped(shared);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		host.TryGetTarget(out _).ShouldBeFalse();
		GC.KeepAlive(shared);
	}

	// A host that drew `source` and text (the default atlas page), then was dropped by its owner.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static WeakReference<UIHost> DrawnAndDropped(ImageSource source)
	{
		var panel = new StackPanel();
		panel.Children.Add(new TextBlock { Text = "Hi", FontSize = 12f });
		panel.Children.Add(new Image { Source = source, Width = 4f, Height = 4f });
		var (host, _) = Hosted(panel);
		Frame(host);
		host.DumpTextures().ShouldStartWith("Textures held (2):\n");
		return new WeakReference<UIHost>(host);
	}

	[Test]
	public void Disposing_an_image_nothing_draws_reports_nothing()
	{
		var (host, _) = Hosted(new Border { Width = 4f, Height = 4f });
		Frame(host);

		BitmapSource.FromRgba8(1, 1, [0, 0, 0, 0]).Dispose();
		Frame(host);

		host.ReleasedTextures.ShouldBeEmpty();
	}

	[Test]
	public void The_rasterizer_drops_the_texels_of_released_sources()
	{
		var source = BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);
		var image = new Image { Source = source, Width = 4f, Height = 4f };
		var (host, view) = Hosted(image);
		var raster = new SoftwareRasterizer(64, 64);
		raster.Render(Frame(host));
		raster.CachedTextureCount.ShouldBe(2); // the white texel and the image

		view.Root.Content = null;
		raster.Render(Frame(host));
		raster.ReleaseTextures(host.ReleasedTextures);

		raster.CachedTextureCount.ShouldBe(1);
	}

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void A_hot_reload_rebuild_releases_what_the_old_tree_drew()
	{
		try
		{
			HotReload.Reset();
			HotReload.Enabled = true;
			var host = new UIHost();
			var view = new ImageSourcesView();
			view.InitializeComponent();
			host.CreateRoot(UILayer.Default).Root.Content = view.Root;
			Frame(host, 64f, 96f);
			var oldStill = view.Still.Source!;

			HotReload.NotifyUpdate([typeof(ImageSourcesView)]);
			Frame(host, 64f, 96f);

			view.Still.Source.ShouldNotBeSameAs(oldStill);
			// The rebuilt tree decoded a new still image; the bitmap and the host image it shares with the old tree stay.
			host.ReleasedTextures.ShouldBe([oldStill]);
			host.DrawLists[0].Data.TextureSlots.ShouldContain(view.LiveBitmap);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Dumps_describe_each_source()
	{
		var view = new ImageSourcesView();
		view.InitializeComponent();
		var (host, _) = Hosted(view.Root);

		var draw = Frame(host, 64f, 96f);

		draw.Dump().ShouldContain("""
			  Textures (4):
			    [0] White 1x1 v0 premultiplied linear
			    [1] BitmapSource 1x1 v0 straight srgb
			    [2] WriteableBitmap 2x2 v0 straight srgb
			    [3] HostImageSource "minimap" 32x16 v0 premultiplied linear

			""".Replace("\r\n", "\n"));
		view.Dot.Visual.Dump().ShouldContain("fill=image(WriteableBitmap 2x2 v0 straight srgb Fill)");
		host.DumpTextures().ShouldBe("""
			Textures held (3):
			  BitmapSource 1x1 v0 straight srgb surfaces=1
			  WriteableBitmap 2x2 v0 straight srgb surfaces=1
			  HostImageSource "minimap" 32x16 v0 premultiplied linear surfaces=1
			Textures released (0):

			""".Replace("\r\n", "\n"));
	}

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Rewriting_a_writeable_bitmap_every_frame_allocates_nothing()
	{
		// The sample shows the bitmap in an image and in an ellipse's brush, beside a host image. Each frame writes a
		// texel and invalidates the bitmap: the host sees the texture revision move, and the rasterizer copies the texels
		// over the ones it kept.
		var view = new ImageSourcesView();
		view.InitializeComponent();
		var (host, _) = Hosted(view.Root);
		var raster = new SoftwareRasterizer(64, 96);
		var frame = 0;
		void Step()
		{
			view.LiveBitmap.PixelBuffer.Span[0] = (byte)frame++;
			view.LiveBitmap.Invalidate();
			host.Tick(0.016, 0f, new Vector2(64f, 96f));
			host.Render();
			raster.Render(host.DrawLists[0].Data);
		}

		for (var i = 0; i < 8; i++)
			Step();
		var textureRevision = host.DrawLists[0].Data.TextureRevision;
		const int measured = 64;

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < measured; i++)
				Step();
		});

		host.DrawLists[0].Data.TextureRevision.ShouldBeGreaterThanOrEqualTo(textureRevision + measured);
		host.ReleasedTextures.ShouldBeEmpty();
	}
}
