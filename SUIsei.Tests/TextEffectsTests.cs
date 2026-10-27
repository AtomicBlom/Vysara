using System.Numerics;
using System.Text;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Extensions;
using SUIsei.Media;
using SUIsei.Media.Imaging;
using SUIsei.Rasterizer;
using SUIsei.Tests.Sample;
using SUIsei.Text.OpenType;

namespace SUIsei.Tests;

/// <summary>
/// <c>suisei:TextEffects</c>: an outline and a shadow on a <see cref="TextBlock"/>, sized in EM and handed to its
/// <see cref="GlyphRunContent"/>. A size is checked against the text's font where it enters (set on text in a tree, or
/// as text joins a root as its content, or before a root's font changes), and an animation's own values as it begins,
/// never mid-animation. Animating them recomposites with no layout, no shaping and no allocation.
/// </summary>
public sealed class TextEffectsTests
{
	private static readonly HdrColor Black = HdrColor.FromRgb(0, 0, 0);
	private static readonly HdrColor White = HdrColor.FromRgb(255, 255, 255);

	private static float Reach => FontAtlas.Default.DistanceRangeEm / 2f;

	// The default atlas with its distance range rewritten, for a font whose field holds more or less than the default's.
	private static FontAtlas AtlasWithRange(int distanceRange)
	{
		using var json = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.font.json")!;
		using var png = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.0.png")!;
		var text = new StreamReader(json).ReadToEnd();
		var rewritten = text.Replace("\"distanceRange\":12", $"\"distanceRange\":{distanceRange}", StringComparison.Ordinal);
		rewritten.ShouldNotBe(text);
		return FontAtlas.Load(new MemoryStream(Encoding.UTF8.GetBytes(rewritten)), png);
	}

	private static TextBlock InRoot(out UIRoot root, string text = "Go")
	{
		var block = new TextBlock { Text = text, FontSize = 20f };
		root = new UIRoot(new DrawListId(0)) { Content = block };
		return block;
	}

	private static DrawListData Render(UIRoot root, float w = 200f, float h = 60f)
	{
		root.LayoutPass(new Size(w * root.LayoutScale, h * root.LayoutScale));
		return root.Render();
	}

	private static Storyboard StoryboardOf(params Timeline[] children)
	{
		var sb = new Storyboard();
		foreach (var child in children)
			sb.Children.Add(child);
		return sb;
	}

	[Test]
	public void Effects_set_on_a_text_block_reach_its_run_and_its_dump()
	{
		var text = InRoot(out var root);
		TextEffects.SetOutlineThickness(text, 0.08f);
		TextEffects.SetOutlineBrush(text, new SolidColorBrush(Black));
		TextEffects.SetShadowOffset(text, new Point(0.05f, 0.1f));
		TextEffects.SetShadowSoftness(text, 0.2f);
		TextEffects.SetShadowBrush(text, new SolidColorBrush(Black) { Opacity = 0.5f });
		var draw = Render(root);

		text.Visual.Dump().ShouldContain(" outline=0.08(0,0,0,1) shadow=0.05,0.1(0,0,0,1)*0.5 softness=0.2\n");
		draw.Vertices.Length.ShouldBe(3 * 2 * 4); // a shadow, an outline and a glyph quad for each glyph
	}

	[Test]
	public void A_size_its_font_cannot_hold_throws_when_set_and_changes_nothing()
	{
		var text = InRoot(out _);
		TextEffects.SetOutlineThickness(text, Reach); // the field's whole reach is allowed
		var thick = Should.Throw<ArgumentOutOfRangeException>(() => TextEffects.SetOutlineThickness(text, Reach + 0.01f));
		thick.Message.ShouldContain("TextBlock \"Go\"");
		thick.Message.ShouldContain("0.188 EM");
		TextEffects.GetOutlineThickness(text).ShouldBe(Reach);

		TextEffects.SetShadowSoftness(text, 2f * Reach);
		Should.Throw<ArgumentOutOfRangeException>(() => TextEffects.SetShadowSoftness(text, 2f * Reach + 0.01f));
		TextEffects.GetShadowSoftness(text).ShouldBe(2f * Reach);
	}

	[Test]
	public void A_negative_or_non_finite_size_or_an_image_brush_throws_even_outside_a_tree()
	{
		var text = new TextBlock { Text = "Go" };
		Should.Throw<ArgumentOutOfRangeException>(() => TextEffects.SetOutlineThickness(text, -0.01f));
		Should.Throw<ArgumentOutOfRangeException>(() => TextEffects.SetShadowSoftness(text, float.NaN));
		Should.Throw<ArgumentOutOfRangeException>(() => TextEffects.SetShadowOffset(text, new Point(float.PositiveInfinity, 0f)));
		var image = new ImageBrush { ImageSource = BitmapSource.FromRgba8(1, 1, new byte[4]) };
		Should.Throw<NotSupportedException>(() => TextEffects.SetOutlineBrush(text, image));
		Should.Throw<NotSupportedException>(() => TextEffects.SetShadowBrush(text, image));
		TextEffects.GetOutlineBrush(text).ShouldBeNull();
	}

	[Test]
	public void Text_set_up_outside_a_tree_is_checked_as_it_becomes_a_roots_content()
	{
		// Outside a tree the font is not known, so a thick outline is taken, and checked against the root's font as the
		// text becomes its content: refused by the default's field, without the root or the text changing, and taken by a
		// font baked with twice the range.
		var text = new TextBlock { Text = "Go" };
		TextEffects.SetOutlineThickness(text, 1.5f * Reach);
		TextEffects.SetOutlineBrush(text, new SolidColorBrush(Black));
		var panel = new StackPanel();
		panel.Children.Add(text);

		var root = new UIRoot(new DrawListId(0));
		Should.Throw<ArgumentOutOfRangeException>(() => root.Content = panel);
		root.Content.ShouldBeNull();
		text.AttachedRoot.ShouldBeNull();

		var wide = new UIRoot(new DrawListId(1)) { Font = AtlasWithRange(24) };
		wide.Content = panel;
		text.AttachedRoot.ShouldBe(wide);
		Render(wide).Vertices.Span[0].Custom.Y.ShouldBe(0.375f, 1e-5f); // 1.5 × 0.1875 EM over the wide font's 0.75 EM range
	}

	[Test]
	public void A_roots_font_is_checked_against_its_text_before_it_changes()
	{
		var text = InRoot(out var root);
		TextEffects.SetOutlineThickness(text, 0.15f);
		var before = root.Font;
		var narrow = AtlasWithRange(4); // a field reaching 0.0625 EM, as the default's did before X1
		Should.Throw<ArgumentOutOfRangeException>(() => root.Font = narrow).Message.ShouldContain("0.063 EM");
		root.Font.ShouldBeSameAs(before);

		TextEffects.SetOutlineThickness(text, 0.05f);
		root.Font = narrow;
		root.Font.ShouldBe(narrow);
		Should.Throw<ArgumentOutOfRangeException>(() => TextEffects.SetOutlineThickness(text, 0.07f)); // now against the new font
	}

	[Test]
	public void An_animation_is_checked_as_it_begins()
	{
		var text = InRoot(out _);
		var clock = new AnimationClock();
		Should.Throw<ArgumentOutOfRangeException>(() => clock.Begin(StoryboardOf(new DoubleAnimation
		{
			To = 2.0 * Reach,
			Duration = TimeSpan.FromSeconds(1),
			Target = text,
			TargetProperty = TextEffects.OutlineThicknessProperty,
		})));
		Should.Throw<ArgumentOutOfRangeException>(() => clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = -0.1,
			To = 0.1,
			Duration = TimeSpan.FromSeconds(1),
			Target = text,
			TargetProperty = TextEffects.OutlineThicknessProperty,
		})));

		var keyed = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1), Target = text, TargetProperty = TextEffects.ShadowSoftnessProperty };
		keyed.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.1 });
		keyed.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.5), Value = 3.0 * Reach });
		keyed.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(1), Value = 0.1 });
		Should.Throw<ArgumentOutOfRangeException>(() => clock.Begin(StoryboardOf(keyed)));
		TextEffects.GetShadowSoftness(text).ShouldBe(0f);
	}

	[Test]
	public void An_easing_that_overshoots_the_field_never_throws_and_draws_clamped()
	{
		// To the field's whole reach with a BackEase, which overshoots it on the way: no throw mid-animation, and the run
		// draws the outline at the most its field holds.
		var text = InRoot(out var root);
		TextEffects.SetOutlineBrush(text, new SolidColorBrush(Black));
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = 0.0,
			To = Reach,
			Duration = TimeSpan.FromSeconds(1),
			Target = text,
			TargetProperty = TextEffects.OutlineThicknessProperty,
			EasingFunction = new BackEase { Amplitude = 1.0, EasingMode = EasingMode.EaseOut },
		}));

		var overshot = false;
		for (var t = 0.05; t < 1.0; t += 0.05)
		{
			clock.Advance(0.05);
			var draw = Render(root);
			if (TextEffects.GetOutlineThickness(text) <= Reach)
				continue;
			overshot = true;
			draw.Vertices.Span[0].Custom.Y.ShouldBe(0.5f - 0.5f / FontAtlas.Default.ScreenPxRange(20f), 1e-5f);
		}
		overshot.ShouldBeTrue();
	}

	[Test]
	public void Warm_frames_animating_the_outline_and_the_shadow_offset_allocate_nothing()
	{
		var text = new TextBlock { Text = "Lap 3", FontSize = 24f, Foreground = new SolidColorBrush(White) };
		TextEffects.SetOutlineBrush(text, new SolidColorBrush(Black));
		TextEffects.SetShadowBrush(text, new SolidColorBrush(Black) { Opacity = 0.5f });
		TextEffects.SetShadowSoftness(text, 0.15f);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = text;
		host.Clock.Begin(StoryboardOf(
			new DoubleAnimation
			{
				From = 0.02,
				To = 0.12,
				Duration = TimeSpan.FromSeconds(0.5),
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				Target = text,
				TargetProperty = TextEffects.OutlineThicknessProperty,
			},
			new PointAnimation
			{
				From = new Point(0f, 0f),
				To = new Point(0.1f, 0.1f),
				Duration = TimeSpan.FromSeconds(0.3),
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				Target = text,
				TargetProperty = TextEffects.ShadowOffsetProperty,
			}));

		const int warm = 16;
		const int measured = 64;
		for (var i = 0; i < warm; i++)
			Frame();
		var run = text.Visual.Content.ShouldBeOfType<GlyphRunContent>();
		var shaped = run.Shape();
		var revision = host.DrawLists[0].Data.Revision;

		var windows = 0u;
		Allocations.ShouldAllocateNothing(() =>
		{
			windows++;
			for (var i = 0; i < measured; i++)
				Frame();
		});

		host.DrawLists[0].Data.Revision.ShouldBe(revision + measured * windows); // every frame recomposited
		run.Shape().ShouldBeSameAs(shaped);
		text.MeasureDirty.ShouldBeFalse();
		run.OutlineThickness.ShouldBeInRange(0.02f, 0.12f);

		void Frame()
		{
			host.Tick(1.0 / 60.0, 0f, new Vector2(200f, 60f));
			host.Render();
		}
	}

	[Test]
	public void A_recoloured_effect_brush_repaints_in_place()
	{
		var text = InRoot(out var root);
		var outline = new SolidColorBrush(Black);
		TextEffects.SetOutlineThickness(text, 0.1f);
		TextEffects.SetOutlineBrush(text, outline);
		Render(root);
		var run = text.Visual.Content.ShouldBeOfType<GlyphRunContent>();

		text.Visual.IsDirty = false;
		outline.Color = HdrColor.FromRgb(255, 0, 0);
		text.Visual.IsDirty.ShouldBeTrue();
		text.MeasureDirty.ShouldBeFalse();
		text.Visual.Content.ShouldBeSameAs(run);
		Render(root).Vertices.Span[0].Color.ToVector4().ShouldBe(new Vector4(1f, 0f, 0f, 1f)); // the outline's quads come first
	}

	[Test]
	[Arguments(1f)]
	[Arguments(2f)]
	public void An_outline_scales_with_the_text(float layoutScale)
	{
		// A black outline 0.1 EM thick round a white '|' at 20 DIPs: 2 DIPs at any layout scale, so 2 or 4 surface pixels.
		const float fontSize = 20f;
		var text = new TextBlock { Text = "|", FontSize = fontSize, Foreground = new SolidColorBrush(White) };
		TextEffects.SetOutlineThickness(text, 0.1f);
		TextEffects.SetOutlineBrush(text, new SolidColorBrush(Black));
		var canvas = new Canvas();
		canvas.Children.Add(text);
		Canvas.SetLeft(text, 10f);
		var root = new UIRoot(new DrawListId(0)) { Content = canvas, LayoutScale = layoutScale };
		var draw = Render(root, 40f, 30f);
		var rasterizer = new SoftwareRasterizer((int)(40f * layoutScale), (int)(30f * layoutScale));
		rasterizer.Render(draw);

		var font = OpenTypeFont.Load(MsdfGenerationTests.CascadiaCode);
		var bar = font.GetOutline(font.GetGlyphIndex('|')).ComputeBounds();
		var edge = (10f + (float)(bar.Left / font.UnitsPerEm) * fontSize) * layoutScale;
		var reach = 0.1f * fontSize * layoutScale;
		var row = (int)((-FontAtlas.Default.Ascender - 0.35f) * fontSize * layoutScale);
		var inside = rasterizer.ReadLinear((int)(edge - reach + 0.5f + 0.5f), row);
		Vector4.Distance(inside, new Vector4(0f, 0f, 0f, 1f)).ShouldBeLessThan(1e-4f); // opaque black just inside the reach
		rasterizer.ReadLinear((int)(edge - reach - 1.5f), row).W.ShouldBe(0f); // nothing past it
	}

	[Test]
	public void The_sample_view_draws_an_outline_and_a_soft_shadow()
	{
		var view = new TextEffectsView();
		view.InitializeComponent();
		var root = new UIRoot(new DrawListId(0)) { Content = view.Root };
		var draw = Render(root, 300f, 120f);

		view.Outlined.Visual.Dump().ShouldContain(" outline=0.1(0,0,0,1)");
		view.Shadowed.Visual.Dump().ShouldContain(" shadow=0.05,0.08(0,0,0,0.502) softness=0.2");

		// Left of the stem of the 'L' that starts "LAP": the outline's black, then nothing past it.
		var rasterizer = new SoftwareRasterizer(300, 120);
		rasterizer.Render(draw);
		var font = OpenTypeFont.Load(MsdfGenerationTests.CascadiaCode);
		var stem = font.GetOutline(font.GetGlyphIndex('L')).ComputeBounds();
		var edge = (float)(stem.Left / font.UnitsPerEm) * 40f;
		var row = (int)((-FontAtlas.Default.Ascender - 0.3f) * 40f);
		Vector4.Distance(rasterizer.ReadLinear((int)(edge - 2f), row), new Vector4(0f, 0f, 0f, 1f)).ShouldBeLessThan(1e-4f);
		Vector4.Distance(rasterizer.ReadLinear((int)(edge + 1.5f), row), new Vector4(1f, 1f, 1f, 1f)).ShouldBeLessThan(1e-4f);
	}
}
