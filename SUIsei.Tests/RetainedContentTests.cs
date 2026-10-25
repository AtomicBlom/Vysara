using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// Composition content is retained and versioned: an element keeps one content object and updates it in place, so a
/// brush change, an animation frame or a re-arrange allocates nothing, and <see cref="VisualContent.Version"/> moves
/// exactly when what the content draws changes.
/// </summary>
public sealed class RetainedContentTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(220, 40, 40);
	private static readonly HdrColor Blue = HdrColor.FromRgb(40, 40, 220);

	[Test]
	public void A_colour_animation_an_opacity_animation_and_a_re_arrange_allocate_nothing_once_warm()
	{
		var animated = new Border { Width = 40f, Height = 20f, Background = Red };
		var fading = new Border { Width = 40f, Height = 20f, Background = Blue };
		var label = new TextBlock { Text = "Steady", FontSize = 16f };
		var framed = new Border { Background = HdrColor.FromRgb(30, 30, 30), Padding = new Thickness(4f), Child = label };
		var panel = new StackPanel { Background = HdrColor.FromRgb(10, 10, 10) };
		panel.Children.Add(animated);
		panel.Children.Add(fading);
		panel.Children.Add(framed);
		panel.Children.Add(new Image { Source = ImageSource.FromRgba8(2, 2, new byte[16]), Width = 8f, Height = 8f });
		var checkBox = new CheckBox { Content = "Templated" };
		panel.Children.Add(checkBox);

		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = panel;

		// The colour animation runs through the storyboard channel a XAML Background animation resolves to. The
		// opacity animation writes the visual directly: a storyboard on the Opacity dependency property still boxes
		// its value into the property store.
		host.Clock.Begin(new Storyboard
		{
			Children =
			{
				new ColorAnimation { From = Red, To = Blue, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, Target = animated, TargetProperty = "Background" },
				new DoubleAnimation
				{
					From = 1.0, To = 0.25, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
					Apply = v =>
					{
						fading.Visual.Opacity = (float)v;
						fading.Visual.MarkDirty();
					},
				},
			},
		});

		const int warm = 8;
		const int measured = 64;
		for (var i = 0; i < warm; i++)
			Frame();

		var content = animated.Visual.Content.ShouldBeOfType<RectContent>();
		var versionBefore = content.Version;
		var revisionBefore = host.DrawLists[0].Data.Revision;

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < measured; i++)
			Frame();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0L);
		// The same content object was recoloured in place every frame, and every frame recomposited.
		animated.Visual.Content.ShouldBeSameAs(content);
		content.Version.ShouldBeGreaterThanOrEqualTo(versionBefore + measured);
		host.DrawLists[0].Data.Revision.ShouldBe(revisionBefore + measured);

		void Frame()
		{
			// Re-arrange the label, a templated control and every element above them, with their children unchanged.
			label.InvalidateArrange();
			checkBox.InvalidateArrange();
			host.Tick(1.0 / 60.0, 0f, new Vector2(200f, 200f));
			host.Render();
		}
	}

	[Test]
	public void An_element_updates_its_content_in_place_and_the_version_moves_only_on_change()
	{
		var border = new Border { Width = 40f, Height = 20f, Background = Red };
		var root = new UIRoot(new DrawListId(0)) { Content = border };
		root.LayoutPass(new Size(100f, 100f));

		var content = border.Visual.Content.ShouldBeOfType<RectContent>();
		var version = content.Version;

		border.Background = Blue;
		border.Visual.Content.ShouldBeSameAs(content);
		content.Fill.ShouldBe(Blue);
		content.Version.ShouldBeGreaterThan(version);

		// A re-arrange that changes nothing the content draws leaves the version alone.
		version = content.Version;
		border.InvalidateArrange();
		root.LayoutPass(new Size(100f, 100f));
		border.Visual.Content.ShouldBeSameAs(content);
		content.Version.ShouldBe(version);

		// A resize is a change to what it draws.
		border.Width = 60f;
		root.LayoutPass(new Size(100f, 100f));
		border.Visual.Content.ShouldBeSameAs(content);
		content.Size.ShouldBe(new Vector2(60f, 20f));
		content.Version.ShouldBeGreaterThan(version);
	}

	[Test]
	public void Work_derived_from_content_goes_stale_when_the_content_changes()
	{
		var content = new GlyphRunContent("Hello", 16f, Red);
		var derived = new object();
		content.HasCurrentDerived.ShouldBeFalse();

		content.SetDerived(derived);
		content.HasCurrentDerived.ShouldBeTrue();

		content.Update("Hello", 16f, Red); // equal values: nothing changed
		content.HasCurrentDerived.ShouldBeTrue();

		content.Text = "Hello!";
		content.HasCurrentDerived.ShouldBeFalse();
		content.Derived.ShouldBeSameAs(derived); // still here to be refilled in place
	}

	[Test]
	public void A_text_block_recolours_in_place_from_foreground_and_from_a_colour_animation()
	{
		var label = new TextBlock { Text = "Hi", FontSize = 16f, Foreground = Red };
		var root = new UIRoot(new DrawListId(0)) { Content = label };
		root.LayoutPass(new Size(100f, 100f));
		var content = label.Visual.Content.ShouldBeOfType<GlyphRunContent>();

		label.Foreground = Blue; // no relayout needed
		label.Visual.Content.ShouldBeSameAs(content);
		content.Color.ShouldBe(Blue);

		var clock = new AnimationClock();
		clock.Begin(new Storyboard
		{
			Children = { new ColorAnimation { From = Red, To = Red, Target = label, TargetProperty = "Foreground" } },
		});
		clock.Advance(0.5);
		content.Color.ShouldBe(Red);
		label.Visual.Content.ShouldBeSameAs(content);
	}

	[Test]
	public void Reordering_children_relinks_the_visual_children_in_the_new_order()
	{
		var first = new Border { Width = 10f, Height = 10f, Background = Red };
		var second = new Border { Width = 10f, Height = 10f, Background = Blue };
		var panel = new StackPanel();
		panel.Children.Add(first);
		panel.Children.Add(second);
		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		root.LayoutPass(new Size(100f, 100f));
		panel.Visual.Children.ShouldBe([first.Visual, second.Visual]);

		panel.Children.RemoveAt(0);
		panel.Children.Add(first);
		root.LayoutPass(new Size(100f, 100f));
		panel.Visual.Children.ShouldBe([second.Visual, first.Visual]);
	}

	[Test]
	public void An_image_shows_in_the_visual_dump()
	{
		var image = new Image { Source = ImageSource.FromRgba8(4, 2, new byte[32]), Width = 40f, Height = 20f, Stretch = Stretch.Fill };
		var root = new UIRoot(new DrawListId(0)) { Content = image };
		root.LayoutPass(new Size(100f, 100f));

		image.Visual.Dump().ShouldContain("image=4x2 dest=(0,0 40x20)");
	}
}
