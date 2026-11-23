using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Documents;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.HarfBuzz;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The shaped-run cache on <see cref="GlyphRunContent"/>: a run is shaped once and its glyphs kept on the content, keyed
/// by its version, so a surface that recomposites every frame re-reads them instead of re-shaping, and allocates nothing.
/// A content change that leaves the shaping alone (a resize, a new brush) keeps them; a change to the text, font,
/// features or the font's shaper re-shapes, into the same retained list.
/// </summary>
public sealed class ShapedRunCacheTests
{
	// A shaper that counts its calls, around the default one.
	private sealed class CountingShaper : ITextShaper
	{
		private readonly SimpleTextShaper _inner = new();
		public int Shapes { get; private set; }
		public int Measures { get; private set; }

		public float Shape(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features, List<ShapedGlyph> output)
		{
			Shapes++;
			return _inner.Shape(text, source, features, output);
		}

		public float Measure(ReadOnlySpan<char> text, IGlyphSource source, FontFeatureSet features)
		{
			Measures++;
			return _inner.Measure(text, source, features);
		}
	}

	// The default atlas loaded on its own, so installing a shaper on it leaves FontAtlas.Default alone.
	private static FontAtlas StandaloneAtlas(ITextShaper shaper)
	{
		using var json = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.font.json")!;
		using var png = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.0.png")!;
		var atlas = FontAtlas.Load(json, png);
		atlas.Shaper = shaper;
		return atlas;
	}

	private static readonly HdrColor Red = HdrColor.FromRgb(255, 0, 0);
	private static readonly HdrColor Blue = HdrColor.FromRgb(0, 0, 255);

	[Test]
	public void A_surface_recompositing_every_frame_does_not_reshape_and_allocates_nothing()
	{
		// The text's own colour animates and a box beside it moves, so every frame recomposites the surface.
		var shaper = new CountingShaper();
		var paint = new SolidColorBrush(Red);
		var label = new TextBlock { Text = "Lap 1:23.456", FontSize = 16f, Foreground = paint };
		var tabular = new TextBlock { Text = "0123456789", FontSize = 16f };
		Typography.SetNumeralAlignment(tabular, FontNumeralAlignment.Tabular);
		var mover = new Border { Width = 4f, Height = 4f, Background = new SolidColorBrush(Blue) };
		var panel = new StackPanel();
		panel.Children.Add(label);
		panel.Children.Add(tabular);
		panel.Children.Add(mover);
		var host = new UIHost();
		var root = host.CreateRoot(UILayer.Default).Root;
		root.Font = StandaloneAtlas(shaper);
		root.Content = panel;

		const int warm = 8;
		const int measured = 64;
		for (var i = 0; i < warm; i++)
			Frame(i);
		var shapes = shaper.Shapes;
		var measures = shaper.Measures;
		var revision = host.DrawLists[0].Data.Revision;
		shapes.ShouldBe(2); // each run once

		var windows = 0u;
		Allocations.ShouldAllocateNothing(() =>
		{
			windows++;
			for (var i = 0; i < measured; i++)
				Frame(i);
		});

		host.DrawLists[0].Data.Revision.ShouldBe(revision + measured * windows); // every frame recomposited
		shaper.Shapes.ShouldBe(shapes);
		shaper.Measures.ShouldBe(measures);
		label.Visual.Content.ShouldBeOfType<GlyphRunContent>().Shape().Glyphs.Count.ShouldBe("Lap1:23.456".Length);

		void Frame(int i)
		{
			paint.Color = i % 2 == 0 ? Red : Blue;
			mover.Translation = new Vector3(i % 10, 0f, 0f);
			host.Tick(1.0 / 60.0, 0f, new Vector2(200f, 100f));
			host.Render();
		}
	}

	/// <summary>
	/// A line's width in EM doesn't depend on the size, so an animated FontSize passed down from a control re-measures and
	/// redraws the text below it without calling the shaper at all. And measuring and drawing share one shaping: a text
	/// block laid out and drawn shapes each of its lines once between them.
	/// </summary>
	[Test]
	public void An_animated_font_size_re_measures_the_text_below_it_without_shaping()
	{
		var shaper = new CountingShaper();
		var first = new TextBlock { Text = "Lap 1:23.456" };
		var second = new TextBlock { Text = "Best\n1:21.002" };
		var panel = new StackPanel();
		panel.Children.Add(first);
		panel.Children.Add(second);
		var card = new ContentControl { Content = panel };
		var host = new UIHost();
		var root = host.CreateRoot(UILayer.Default).Root;
		root.Font = StandaloneAtlas(shaper);
		root.Content = card;
		host.Tick(0.016, 0f, new Vector2(400f, 300f));
		host.Render();
		shaper.Shapes.ShouldBe(3); // three lines, each shaped once for both measure and draw
		var height = second.DesiredSize.Height;

		host.Clock.Tween(card, Control.FontSizeProperty, 28f, TimeSpan.FromSeconds(2));
		var frame = 0;
		void Frame()
		{
			frame++;
			host.Tick(1.0 / 60, frame / 60f, new Vector2(400f, 300f));
			host.Render();
		}
		for (var i = 0; i < 5; i++)
			Frame();
		// Re-measuring at each new size, and drawing it, allocates nothing either.
		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 5; i++)
				Frame();
		});
		while (frame < 130)
			Frame();


		second.FontSize.ShouldBe(28f);
		second.DesiredSize.Height.ShouldBe(height * 2f, 1e-3f); // re-measured at the new size
		shaper.Shapes.ShouldBe(3);
		shaper.Measures.ShouldBe(0);
	}

	[Test]
	public void A_resize_or_a_new_brush_keeps_the_shaped_run_and_its_inputs_reshape_it()
	{
		var shaper = new CountingShaper();
		var label = new TextBlock { Text = "12", FontSize = 16f };
		var panel = new StackPanel { Width = 100f };
		panel.Children.Add(label);
		var root = new UIRoot(new DrawListId(0)) { Font = StandaloneAtlas(shaper), Content = panel };
		void Frame()
		{
			root.LayoutPass(new Size(200f, 100f));
			root.Render();
		}

		Frame();
		var run = label.Visual.Content.ShouldBeOfType<GlyphRunContent>();
		var shaped = run.Shape();
		shaper.Shapes.ShouldBe(1);

		// What the run occupies and how it is painted: the content changes, the shaping does not.
		var version = run.Version;
		panel.Width = 120f;
		Frame();
		label.Foreground = new SolidColorBrush(Red);
		Frame();
		run.Version.ShouldBeGreaterThan(version);
		shaper.Shapes.ShouldBe(1);

		// What shaping reads: each change shapes again, into the same retained run.
		label.Text = "123";
		Frame();
		shaper.Shapes.ShouldBe(2);
		Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
		Frame();
		shaper.Shapes.ShouldBe(3);
		var other = new CountingShaper();
		root.Font = StandaloneAtlas(other);
		Frame();
		other.Shapes.ShouldBe(1);
		root.Font.Shaper = shaper; // swapped on the font itself: the next composite of the run notices
		label.InvalidateVisual();
		Frame();
		shaper.Shapes.ShouldBe(4);
		run.Shape().ShouldBeSameAs(shaped);
		shaped.Glyphs.Count.ShouldBe(3);
	}

	[Test]
	public void A_cached_run_draws_exactly_what_shaping_it_afresh_draws()
	{
		static (UIRoot Root, TextBlock Text) Scene()
		{
			var text = new TextBlock { Text = "x = 10.5", FontSize = 20f };
			Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
			var root = new UIRoot(new DrawListId(0)) { Content = text };
			root.LayoutPass(new Size(200f, 40f));
			return (root, text);
		}

		static string Body(DrawListData draw)
		{
			var dump = draw.Dump();
			return dump[dump.IndexOf("  Textures", StringComparison.Ordinal)..]; // past the revision header
		}

		var (cached, text) = Scene();
		cached.Render();
		var run = text.Visual.Content.ShouldBeOfType<GlyphRunContent>();
		text.InvalidateVisual(); // recomposite from the cached run
		var again = Body(cached.Render());
		run.HasCurrentDerived.ShouldBeTrue();

		Body(Scene().Root.Render()).ShouldBe(again);
	}

	[Test]
	public void HarfBuzz_shapes_a_warm_run_without_allocating()
	{
		// It shapes into one retained buffer. With features, HarfBuzzSharp's Font.Shape copies them into a new array each
		// call, which this does not cover; the cache above keeps that off steady and animating frames.
		Skip.Unless(OperatingSystem.IsWindows(), "HarfBuzzSharp's native library ships for Windows only (BinaryVibrance.Vysara.HarfBuzz).");
		using var shaper = new HarfBuzzTextShaper(MsdfGenerationTests.CascadiaCode);
		var source = new TabularFiguresTests.AnyGlyph();
		var features = FontFeatureSet.Empty;
		var output = new List<ShapedGlyph>(32);
		for (var i = 0; i < 4; i++)
		{
			output.Clear();
			shaper.Shape("Lap -> 1:23.456", source, features, output);
			shaper.Measure("Lap -> 1:23.456", source, features);
		}

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 64; i++)
			{
				output.Clear();
				shaper.Shape("Lap -> 1:23.456", source, features, output);
				shaper.Measure("Lap -> 1:23.456", source, features);
			}
		});
		output.Count.ShouldBe("Lap -> 1:23.456".Length); // the stub source gives every glyph, spaces too, geometry
	}
}
