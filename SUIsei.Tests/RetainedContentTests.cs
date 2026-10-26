using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// Composition content is retained and versioned: an element keeps one content object and updates it in place, so a
/// brush change, an animation frame or a re-arrange allocates nothing, and <see cref="VisualContent.Version"/> moves
/// exactly when what the content draws changes. Paint is retained too: a brush recolours its one composition brush in
/// place, which moves that brush's own version and leaves the content's alone.
/// </summary>
public sealed class RetainedContentTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(220, 40, 40);
	private static readonly HdrColor Blue = HdrColor.FromRgb(40, 40, 220);
	private static readonly CompositionColorBrush RedPaint = new(Red);

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Animations_tweens_and_a_re_arrange_allocate_nothing_once_warm()
	{
		var animated = new Border { Width = 40f, Height = 20f, Background = new SolidColorBrush(Red) };
		var fading = new Border { Width = 40f, Height = 20f, Background = new SolidColorBrush(Blue) };
		var blinking = new Border { Width = 40f, Height = 20f, Background = new SolidColorBrush(Blue) };
		var tweened = new Border { Width = 40f, Height = 20f, Background = new SolidColorBrush(Red) };
		var sliding = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(Red) };
		var canvas = new Canvas { Width = 100f, Height = 20f };
		canvas.Children.Add(sliding);
		var label = new TextBlock { Text = "Steady", FontSize = 16f };
		var framed = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(30, 30, 30)), Padding = new Thickness(4f), Child = label };
		var checkBox = new CheckBox { Content = "Templated" };

		// A shared brush on two elements, recoloured by one storyboard; a gradient on a rect and on text whose stops change
		// every frame; a render transform moved by a storyboard; a button whose state swaps its background through a
		// transition; and a hundred anchors and a label moved by Translation.
		var shared = new SolidColorBrush(Blue);
		var sharedA = new Border { Width = 20f, Height = 10f, Background = shared };
		var sharedB = new Border { Width = 20f, Height = 10f, Background = shared };
		var stop = new GradientStop { Color = Red, Offset = 0.5f };
		var gradient = new LinearGradientBrush
		{
			EndPoint = new Point(1f, 0f),
			ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation,
			GradientStops = { new GradientStop { Color = Blue }, stop, new GradientStop { Color = Blue, Offset = 1f } },
		};
		var graded = new Border { Width = 60f, Height = 20f, Background = gradient, CornerRadius = new CornerRadius(4f) };
		var gradedText = new TextBlock { Text = "Graded", FontSize = 16f, Foreground = gradient };
		var moved = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(Red), RenderTransform = new CompositeTransform() };
		var button = new Button { Content = "Hover" };
		var tracked = new TextBlock { Text = "Anchor", FontSize = 12f };
		var anchors = new Canvas { Width = 100f, Height = 20f };
		for (var i = 0; i < 100; i++)
			anchors.Children.Add(new Border { Width = 2f, Height = 2f, Background = shared });

		var panel = new StackPanel { Background = new SolidColorBrush(HdrColor.FromRgb(10, 10, 10)) };
		foreach (var child in new UIElement[] { animated, fading, blinking, tweened, canvas, framed, checkBox, sharedA, sharedB, graded, gradedText, moved, button, tracked, anchors })
			panel.Children.Add(child);
		panel.Children.Add(new Image { Source = ImageSource.FromRgba8(2, 2, new byte[16]), Width = 8f, Height = 8f });

		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = panel;

		// Every kind of storyboard animation, each through Target + TargetProperty as generated XAML does: a brush's
		// colour (on an element's own brush and on a shared one), a float dependency property, an attached property, a
		// keyframe animation and a render transform's property.
		var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.5), RepeatBehavior = RepeatBehavior.Forever, Target = blinking, TargetProperty = UIElement.OpacityProperty };
		blink.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
		blink.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.25), Value = 0.5, IsDiscrete = true });
		blink.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.5), Value = 0.2, EasingFunction = new CubicEase() });
		var backgroundColor = new PropertyPath(Border.BackgroundProperty, SolidColorBrush.ColorProperty);
		host.Clock.Begin(new Storyboard
		{
			Children =
			{
				new ColorAnimation { From = Red, To = Blue, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, Target = animated, TargetProperty = backgroundColor },
				new ColorAnimation { To = Red, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, Target = sharedA, TargetProperty = backgroundColor },
				new DoubleAnimation { To = 0.25, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, Target = fading, TargetProperty = UIElement.OpacityProperty },
				new DoubleAnimation { To = 60.0, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, Target = sliding, TargetProperty = Canvas.LeftProperty },
				new DoubleAnimation
				{
					To = 30.0, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, Target = moved,
					TargetProperty = new PropertyPath(UIElement.RenderTransformProperty, CompositeTransform.TranslateXProperty),
				},
				blink,
			},
		});

		// Code-first tweens, restarted every eight frames and finishing in four, so each run returns its entry to the
		// pool and the next rents it back.
		var probe = new TweenProbe();
		var ease = new QuadraticEase();
		var tweenLength = TimeSpan.FromSeconds(4.0 / 60.0);

		const int warm = 16;
		const int measured = 64;
		for (var i = 0; i < warm; i++)
			Frame(i);

		var content = animated.Visual.Content.ShouldBeOfType<RectContent>();
		var paint = content.Fill.ShouldBeOfType<CompositionColorBrush>();
		var versionBefore = content.Version;
		var paintBefore = paint.Version;
		var revisionBefore = host.DrawLists[0].Data.Revision;

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < measured; i++)
			Frame(i);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0L);
		// The same content and paint were recoloured in place every frame, the paint versioning the change, and every
		// frame recomposited.
		animated.Visual.Content.ShouldBeSameAs(content);
		content.Fill.ShouldBeSameAs(paint);
		content.Version.ShouldBe(versionBefore);
		paint.Version.ShouldBeGreaterThanOrEqualTo(paintBefore + measured);
		host.DrawLists[0].Data.Revision.ShouldBe(revisionBefore + measured);
		// The animations and tweens really ran.
		fading.Opacity.ShouldBeInRange(0.25f, 1f);
		fading.Opacity.ShouldNotBe(1f);
		Canvas.GetLeft(sliding).ShouldBeGreaterThan(0f);
		sliding.LayoutRect.X.ShouldBe(Canvas.GetLeft(sliding));
		((CompositeTransform)moved.RenderTransform!).TranslateX.ShouldBeGreaterThan(0f);
		shared.Color.ShouldNotBe(Blue);
		sharedB.ShownColor().ShouldBe(shared.Color); // the shared brush's animation reached its other user
		tweened.Opacity.ShouldBe(0.9f); // where the last tween ended
		tracked.Translation.ShouldBe(Vector3.Zero);
		Frame(measured);                  // starts the next run outward
		probe.Position.X.ShouldBeGreaterThan(0f);
		tracked.Translation.X.ShouldBeGreaterThan(0f);

		void Frame(int i)
		{
			if (i % 8 == 0)
			{
				var forward = i % 16 == 0;
				host.Clock.Tween(probe, static p => p.Position, static (p, v) => p.Position = v, forward ? new Vector2(10f, 20f) : Vector2.Zero, tweenLength, ease);
				host.Clock.Tween(tweened, UIElement.OpacityProperty, forward ? 0.3f : 0.9f, tweenLength);
				host.Clock.Tween(tracked, static e => e.Translation, static (e, v) => e.Translation = v, forward ? new Vector3(40f, 8f, 0f) : Vector3.Zero, tweenLength);
				VisualStateManager.GoToState(button, forward ? "PointerOver" : "Normal", useTransitions: true);
			}
			// A gradient stop moves and recolours every frame; the anchors all move.
			stop.Offset = 0.25f + i % 4 * 0.125f;
			stop.Color = i % 2 == 0 ? Red : Blue;
			var x = i % 10;
			foreach (var anchor in anchors.Children.Items)
				anchor.Translation = new Vector3(x, x * 0.5f, 0f);
			// Re-arrange the label, a templated control and every element above them, with their children unchanged.
			label.InvalidateArrange();
			checkBox.InvalidateArrange();
			host.Tick(1.0 / 60.0, 0f, new Vector2(200f, 400f));
			host.Render();
		}
	}

	[Test]
	public void An_element_updates_its_content_in_place_and_the_version_moves_only_on_change()
	{
		var border = new Border { Width = 40f, Height = 20f, Background = new SolidColorBrush(Red) };
		var root = new UIRoot(new DrawListId(0)) { Content = border };
		root.LayoutPass(new Size(100f, 100f));

		var content = border.Visual.Content.ShouldBeOfType<RectContent>();
		var version = content.Version;

		// A different brush is a different paint: the content takes it and moves its version.
		border.Background = new SolidColorBrush(Blue);
		border.Visual.Content.ShouldBeSameAs(content);
		content.Fill.SolidColor().ShouldBe(Blue);
		content.Version.ShouldBeGreaterThan(version);

		// Recolouring the brush in place changes its paint, not the content.
		version = content.Version;
		var paint = content.Fill!;
		var paintVersion = paint.Version;
		((SolidColorBrush)border.Background!).Color = Red;
		content.Fill.ShouldBeSameAs(paint);
		content.Version.ShouldBe(version);
		paint.Version.ShouldBeGreaterThan(paintVersion);

		// A re-arrange that changes nothing the content draws leaves the version alone.
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
		var content = new GlyphRunContent("Hello", 16f, RedPaint);
		var derived = new object();
		content.HasCurrentDerived.ShouldBeFalse();

		content.SetDerived(derived);
		content.HasCurrentDerived.ShouldBeTrue();

		content.Update("Hello", 16f, RedPaint, Vector2.Zero); // equal values: nothing changed
		content.HasCurrentDerived.ShouldBeTrue();

		content.Text = "Hello!";
		content.HasCurrentDerived.ShouldBeFalse();
		content.Derived.ShouldBeSameAs(derived); // still here to be refilled in place
	}

	[Test]
	public void A_text_block_recolours_in_place_from_foreground_and_from_a_colour_animation()
	{
		var label = new TextBlock { Text = "Hi", FontSize = 16f, Foreground = new SolidColorBrush(Red) };
		var root = new UIRoot(new DrawListId(0)) { Content = label };
		root.LayoutPass(new Size(100f, 100f));
		var content = label.Visual.Content.ShouldBeOfType<GlyphRunContent>();

		label.Foreground = new SolidColorBrush(Blue); // no relayout needed
		label.Visual.Content.ShouldBeSameAs(content);
		content.Brush.SolidColor().ShouldBe(Blue);
		label.ArrangeDirty.ShouldBeFalse();

		var clock = new AnimationClock();
		clock.Begin(new Storyboard
		{
			Children = { new ColorAnimation { From = Red, To = Red, Target = label, TargetProperty = new PropertyPath(TextBlock.ForegroundProperty, SolidColorBrush.ColorProperty) } },
		});
		clock.Advance(0.5);
		content.Brush.SolidColor().ShouldBe(Red);
		label.Visual.Content.ShouldBeSameAs(content);
	}

	[Test]
	public void A_text_block_with_no_foreground_paints_white()
	{
		var label = new TextBlock { Text = "Hi" };
		var root = new UIRoot(new DrawListId(0)) { Content = label };
		root.LayoutPass(new Size(100f, 100f));

		label.Foreground.ShouldBeNull();
		label.ShownColor().ShouldBe(HdrColor.FromRgb(255, 255, 255));
	}

	[Test]
	public void Reordering_children_relinks_the_visual_children_in_the_new_order()
	{
		var first = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(Red) };
		var second = new Border { Width = 10f, Height = 10f, Background = new SolidColorBrush(Blue) };
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
	public void Paint_shows_in_the_visual_dump()
	{
		var image = new Image { Source = ImageSource.FromRgba8(4, 2, new byte[32]), Width = 40f, Height = 20f, Stretch = Stretch.Fill };
		var gradient = new LinearGradientBrush
		{
			ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
			GradientStops = { new GradientStop { Color = new HdrColor(2f, 0f, 0f, 1f) }, new GradientStop { Color = Blue, Offset = 1f } },
		};
		var graded = new Border { Width = 40f, Height = 20f, Background = gradient, BorderBrush = new SolidColorBrush(Red), BorderThickness = new Thickness(2f) };
		var panel = new StackPanel();
		panel.Children.Add(image);
		panel.Children.Add(graded);
		var root = new UIRoot(new DrawListId(0)) { Content = panel };
		root.LayoutPass(new Size(100f, 100f));

		image.Visual.Dump().ShouldContain("rect=40x20 fill=image(4x2 Fill)");
		graded.Visual.Dump().ShouldContain(
			"rect=40x20 fill=linear((0,0)->(1,1) rel linear 0:(2,0,0,1) 1:(0.157,0.157,0.863,1)) border=2(0.863,0.157,0.157,1)");
	}

	// A plain object with a CLR property for a code-first tween to drive.
	private sealed class TweenProbe
	{
		public Vector2 Position { get; set; }
	}
}
