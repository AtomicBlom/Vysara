using System.Text;
using System.Text.Json.Nodes;
using SUIsei.Composition;
using SUIsei.Controls;
using SUIsei.Documents;
using SUIsei.Drawing;
using SUIsei.HarfBuzz;
using SUIsei.Rasterizer;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// Tabular figures, end to end: OpenType feature settings (<see cref="FontFeature"/>, <see cref="FontFeatureSet"/>),
/// the shapers applying them (<see cref="SimpleTextShaper"/> equalises digit advances under <c>tnum</c>; HarfBuzz takes
/// any feature), WinUI's <see cref="Typography.NumeralAlignmentProperty"/> on a <see cref="TextBlock"/>, and the text
/// measuring and drawing with one font and one feature set, its root's.
/// </summary>
public sealed class TabularFiguresTests
{
	// The default atlas given proportional digits: '1' and '7' narrowed, their ink kept centred in their advance, as a
	// font with proportional figures draws them. Cascadia Code's own digits are all one width.
	private static FontAtlas ProportionalDigits() => WithAdvances(new Dictionary<int, double> { ['1'] = 0.35, ['7'] = 0.45 });

	private static FontAtlas WithAdvances(Dictionary<int, double> advances)
	{
		var resources = typeof(FontAtlas).Assembly;
		using var json = resources.GetManifestResourceStream("ui-default.font.json")!;
		var layout = JsonNode.Parse(json)!;
		foreach (var glyph in layout["glyphs"]!.AsArray())
		{
			if (!advances.TryGetValue((int)glyph!["unicode"]!, out var advance))
				continue;
			var shift = ((double)glyph["advance"]! - advance) / 2;
			glyph["advance"] = advance;
			var bounds = glyph["planeBounds"]!;
			bounds["left"] = (double)bounds["left"]! - shift;
			bounds["right"] = (double)bounds["right"]! - shift;
		}

		using var png = resources.GetManifestResourceStream("ui-default.0.png")!;
		return FontAtlas.Load(new MemoryStream(Encoding.UTF8.GetBytes(layout.ToJsonString())), png);
	}

	private const float Widest = 0.5859375f; // every other digit's advance, in EM

	[Test]
	public void A_feature_tag_packs_its_four_characters_and_prints_in_HarfBuzz_syntax()
	{
		FontFeature.TabularFigures.Tag.ShouldBe(0x746E756Du); // 't' 'n' 'u' 'm'
		FontFeature.TagOf("ss1").ShouldBe(0x73733120u);      // padded with a space
		FontFeature.TabularFigures.ToString().ShouldBe("tnum");
		new FontFeature("calt", 0).ToString().ShouldBe("calt=0");
		Should.Throw<ArgumentException>(() => FontFeature.TagOf("toolong"));
		Should.Throw<ArgumentException>(() => FontFeature.TagOf("tést"));
	}

	[Test]
	public void A_feature_set_keeps_the_last_setting_of_each_tag_and_compares_by_value()
	{
		var set = new FontFeatureSet(new FontFeature("liga", 0), FontFeature.TabularFigures, new FontFeature("liga", 1));
		set.ToString().ShouldBe("liga tnum");
		set.IsOn(FontFeature.TabularFigures.Tag).ShouldBeTrue();
		new FontFeatureSet(FontFeature.TabularFigures).ShouldBe(FontFeatureSet.TabularFigures);
		new FontFeatureSet(new FontFeature("tnum", 0)).IsOn(FontFeature.TabularFigures.Tag).ShouldBeFalse();
		FontFeatureSet.Empty.IsEmpty.ShouldBeTrue();
	}

	/// <summary>Two sets that set the same tags to the same values are equal, with one hash, however the settings were
	/// ordered: the shaping cache keys on the set, so the same text with the same features shapes once.</summary>
	[Test]
	public void A_feature_set_compares_equal_whatever_order_its_settings_were_written_in()
	{
		var a = new FontFeatureSet(new FontFeature("tnum"), new FontFeature("liga", 0));
		var b = new FontFeatureSet(new FontFeature("liga", 0), new FontFeature("tnum"));

		a.ShouldBe(b);
		a.GetHashCode().ShouldBe(b.GetHashCode());
	}

	[Test]
	public void Tabular_figures_give_every_digit_the_widest_digit_s_advance_centred_in_it()
	{
		var font = ProportionalDigits();
		var output = new List<ShapedGlyph>();
		var width = font.Shaper.Shape("1a7", font, FontFeatureSet.TabularFigures, output);

		output[0].PenXEm.ShouldBe(0f);
		output[0].XOffsetEm.ShouldBe((Widest - 0.35f) / 2, 1e-6f);     // the '1' centred in the wider advance
		output[1].PenXEm.ShouldBe(Widest, 1e-6f);                      // the letter after it keeps its own
		output[1].XOffsetEm.ShouldBe(0f);
		output[2].PenXEm.ShouldBe(2 * Widest, 1e-6f);
		output[2].XOffsetEm.ShouldBe((Widest - 0.45f) / 2, 1e-6f);
		width.ShouldBe(3 * Widest, 1e-6f);
		font.MeasureWidth("1a7", FontFeatureSet.TabularFigures).ShouldBe(width, 1e-6f);
	}

	[Test]
	public void Without_tnum_digits_keep_their_own_advances()
	{
		var font = ProportionalDigits();
		foreach (var features in new[] { FontFeatureSet.Empty, FontFeatureSet.ProportionalFigures, new FontFeatureSet(new FontFeature("tnum", 0)) })
		{
			var output = new List<ShapedGlyph>();
			font.Shaper.Shape("17", font, features, output).ShouldBe(0.35f + 0.45f, 1e-6f);
			output[1].PenXEm.ShouldBe(0.35f, 1e-6f);
			output[1].XOffsetEm.ShouldBe(0f);
		}
	}

	[Test]
	public void NumeralAlignment_shapes_a_text_block_s_measure_and_its_run_with_the_same_features()
	{
		var text = new TextBlock { Text = "1111", FontSize = 20f };
		var panel = new StackPanel();
		panel.Children.Add(text);
		var root = new UIRoot(new DrawListId(0)) { Content = panel, Font = ProportionalDigits() };
		root.LayoutPass(new Size(400f, 100f));
		text.DesiredSize.Width.ShouldBe(4 * 0.35f * 20f, 1e-3f);

		Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
		root.LayoutPass(new Size(400f, 100f));
		text.DesiredSize.Width.ShouldBe(4 * Widest * 20f, 1e-3f);

		var run = text.Visual.Content.ShouldBeOfType<GlyphRunContent>();
		run.Features.ShouldBeSameAs(FontFeatureSet.TabularFigures);
		run.Font.ShouldBeSameAs(root.Font);
		text.Visual.Dump().ShouldContain("glyphs=\"1111\" size=20 features=\"tnum\"");

		// The run draws where it was measured: the last '1' is centred in the last tabular advance.
		var draw = root.Render();
		var vertices = draw.Vertices.Span;
		root.Font.TryGetGlyph('1', out var one);
		var lastLeft = vertices[12].Position.X; // the fourth quad's top-left corner
		lastLeft.ShouldBe((3 * Widest + (Widest - 0.35f) / 2 + one.PlaneLeft) * 20f, 1e-3f);
	}

	[Test]
	public void Text_measures_and_draws_with_its_root_s_font_and_measures_again_when_it_changes()
	{
		var text = new TextBlock { Text = "11", FontSize = 20f };
		var panel = new StackPanel();
		panel.Children.Add(text);
		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		root.LayoutPass(new Size(400f, 100f));
		text.DesiredSize.Width.ShouldBe(2 * Widest * 20f, 1e-3f);
		root.Render().TextureSlots[1].ShouldBeSameAs(FontAtlas.Default.Image);

		var narrow = ProportionalDigits();
		root.Font = narrow;
		root.LayoutPass(new Size(400f, 100f));
		text.DesiredSize.Width.ShouldBe(2 * 0.35f * 20f, 1e-3f);
		var draw = root.Render();
		draw.TextureSlots[1].ShouldBeSameAs(narrow.Image); // the page the glyphs were laid out against
		text.Visual.Content.ShouldBeOfType<GlyphRunContent>().Font.ShouldBeSameAs(narrow);
	}

	[Test]
	public void Tabular_digits_of_different_widths_land_at_the_same_steps_in_pixels()
	{
		// Through markup, the compositor and the CPU rasterizer: the ink of each '1' and each '8' on the tabular lines is
		// centred at the same x, one tabular advance apart, while proportional '1's pack closer.
		const float size = 32f;
		var view = new TabularFiguresView();
		view.InitializeComponent();
		var root = new UIRoot(new DrawListId(0)) { Content = view.Root, Font = ProportionalDigits() };
		root.LayoutPass(new Size(160f, 140f));
		var rasterizer = new SoftwareRasterizer(160, 140);
		rasterizer.Render(root.Render());

		var ones = InkCentres(rasterizer, view.TabularOnes.LayoutRect, Widest * size);
		var eights = InkCentres(rasterizer, view.TabularEights.LayoutRect, Widest * size);
		for (var k = 0; k < 4; k++)
		{
			ones[k].ShouldBe((k + 0.5f) * Widest * size, 0.3f);
			eights[k].ShouldBe(ones[k], 0.3f);
		}

		// The proportional '1's overlap their narrow advances, so take the whole line: four glyphs centred in their
		// advances put its ink's centre two advances in.
		InkCentres(rasterizer, view.ProportionalOnes.LayoutRect, rasterizer.Width, count: 1)[0].ShouldBe(2 * 0.35f * size, 0.3f);
		InkCentres(rasterizer, view.TabularOnes.LayoutRect, rasterizer.Width, count: 1)[0].ShouldBe(2 * Widest * size, 0.3f);
	}

	// The x of the ink centroid in each of `count` bands `advance` wide from the left, along the line the text block
	// occupies.
	private static float[] InkCentres(SoftwareRasterizer rasterizer, Rect line, float advance, int count = 4)
	{
		var centres = new float[count];
		for (var k = 0; k < count; k++)
		{
			double moment = 0, mass = 0;
			for (var x = (int)(k * advance); x < (int)((k + 1) * advance) && x < rasterizer.Width; x++)
			{
				for (var y = (int)line.Y; y < (int)(line.Y + line.Height); y++)
				{
					var a = rasterizer.ReadLinear(x, y).W;
					moment += a * (x + 0.5);
					mass += a;
				}
			}

			centres[k] = (float)(moment / mass);
		}

		return centres;
	}

	// Any glyph id resolves, so the HarfBuzz shaper's output can be read without an atlas holding every glyph it picks.
	internal sealed class AnyGlyph : IGlyphSource
	{
		public bool TryGetGlyph(int codepoint, out Glyph glyph) { glyph = default; return false; }
		public bool TryGetGlyphById(int glyphId, out Glyph glyph)
		{
			glyph = new Glyph(-1, glyphId, 0.5f, HasGeometry: true, 0f, -0.7f, 0.5f, 0f, 0f, 0f, 1f, 1f);
			return true;
		}

		public bool TryGetGlyphId(int codepoint, out int glyphId) { glyphId = -1; return false; }
		public bool HasGlyphIds => true;
		public float Advance(int codepoint) => 0.5f;
		public float Ascender => -0.9f;
		public float LineHeight => 1.2f;
	}

	[Test]
	public void Features_reach_HarfBuzz_for_the_whole_run()
	{
		Skip.Unless(OperatingSystem.IsWindows(), "HarfBuzzSharp's native library ships for Windows only (SUIsei.HarfBuzz).");
		using var shaper = new HarfBuzzTextShaper(MsdfGenerationTests.CascadiaCode);
		var source = new AnyGlyph();
		int[] Ids(string text, FontFeatureSet features)
		{
			var output = new List<ShapedGlyph>();
			shaper.Shape(text, source, features, output);
			return [.. output.Select(g => g.Glyph.GlyphId)];
		}

		// Cascadia Code's slashed zero (zero) and its programming-arrow alternates (calt, on by default).
		Ids("0", new FontFeatureSet(new FontFeature("zero"))).ShouldNotBe(Ids("0", FontFeatureSet.Empty));
		Ids("->", new FontFeatureSet(new FontFeature("calt", 0))).ShouldNotBe(Ids("->", FontFeatureSet.Empty));

		// Its digits are tabular by design, so tnum keeps them one advance apart; measuring agrees with shaping.
		var digits = new List<ShapedGlyph>();
		var width = shaper.Shape("0123456789", source, FontFeatureSet.TabularFigures, digits);
		for (var i = 1; i < digits.Count; i++)
			(digits[i].PenXEm - digits[i - 1].PenXEm).ShouldBe(Widest, 1e-4f);
		shaper.Measure("0123456789", source, FontFeatureSet.TabularFigures).ShouldBe(width, 1e-5f);
	}
}
