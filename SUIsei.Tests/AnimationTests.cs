using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// Animation coverage: normalized-time math, per-type interpolation + easing, and the two
/// apply paths — the DP animation rung (Opacity) vs the no-relayout visual channels (RenderTransform translate,
/// brush). All deterministic from synthetic <c>deltaSeconds</c>, asserted on CPU with no GPU.
/// </summary>
public sealed class AnimationTests
{
	private static Storyboard StoryboardOf(params Timeline[] children)
	{
		var sb = new Storyboard();
		foreach (var c in children)
			sb.Children.Add(c);
		return sb;
	}

	[Test]
	public void Double_animation_lerps_linearly_over_the_duration()
	{
		var applied = 0.0;
		var anim = new DoubleAnimation
		{
			From = 0,
			To = 10,
			Duration = TimeSpan.FromSeconds(1),
			Apply = v => applied = v,
		};
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(anim));

		clock.Advance(0.25);
		applied.ShouldBe(2.5, 0.0001);
		clock.Advance(0.25); // now at 0.5s
		applied.ShouldBe(5.0, 0.0001);
		clock.Advance(0.5); // 1.0s — completes
		applied.ShouldBe(10.0, 0.0001);
	}

	[Test]
	public void Cubic_ease_in_shapes_the_progress()
	{
		var applied = 0.0;
		var anim = new DoubleAnimation
		{
			From = 0,
			To = 1,
			Duration = TimeSpan.FromSeconds(1),
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
			Apply = v => applied = v,
		};
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(anim));
		clock.Advance(0.5);
		applied.ShouldBe(0.125, 0.0001); // 0.5^3
	}

	[Test]
	public void Color_and_point_animations_interpolate_componentwise()
	{
		HdrColor color = default;
		Point point = default;
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(
			new ColorAnimation { From = new HdrColor(0, 0, 0, 1), To = new HdrColor(1, 1, 1, 1), Duration = TimeSpan.FromSeconds(1), Apply = v => color = v },
			new PointAnimation { From = new Point(0, 0), To = new Point(20, 40), Duration = TimeSpan.FromSeconds(1), Apply = v => point = v }));

		clock.Advance(0.5);
		color.ShouldBe(new HdrColor(0.5f, 0.5f, 0.5f, 1f));
		point.ShouldBe(new Point(10f, 20f));
	}

	[Test]
	public void Keyframes_segment_with_per_key_easing()
	{
		var applied = 0.0;
		var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1), Apply = v => applied = v };
		anim.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
		anim.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.5), Value = 100 });
		anim.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(1.0), Value = 200 });

		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(anim));

		clock.Advance(0.25); // halfway through the first (linear) segment 0→100
		applied.ShouldBe(50.0, 0.0001);
		clock.Advance(0.5); // 0.75s — halfway through the second segment 100→200
		applied.ShouldBe(150.0, 0.0001);
	}

	[Test]
	public void Auto_reverse_ping_pongs_within_one_iteration()
	{
		var applied = 0.0;
		var anim = new DoubleAnimation
		{
			From = 0,
			To = 100,
			Duration = TimeSpan.FromSeconds(1),
			AutoReverse = true,
			Apply = v => applied = v,
		};
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(anim));

		clock.Advance(0.5); // forward half
		applied.ShouldBe(50.0, 0.0001);
		clock.Advance(0.5); // peak (end of forward leg)
		applied.ShouldBe(100.0, 0.0001);
		clock.Advance(0.5); // reversing
		applied.ShouldBe(50.0, 0.0001);
	}

	[Test]
	public void Forever_repeat_keeps_running_and_is_never_dropped()
	{
		var applied = 0.0;
		var anim = new DoubleAnimation
		{
			From = 0,
			To = 1,
			Duration = TimeSpan.FromSeconds(1),
			RepeatBehavior = RepeatBehavior.Forever,
			Apply = v => applied = v,
		};
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(anim));

		clock.Advance(1.5); // 1.5 iterations in
		applied.ShouldBe(0.5, 0.0001);
		clock.HasActiveClocks.ShouldBeTrue();
	}

	// --- Element integration ---

	private static (Border Border, UIRoot Root) LaidOutBorder()
	{
		var border = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(0, 0, 0)), Width = 50, Height = 50 };
		var root = new UIRoot(new DrawListId(0)) { Content = border };
		root.LayoutPass(new Size(100, 100));
		return (border, root);
	}

	[Test]
	public void Opacity_animates_through_the_dp_rung_without_relayout()
	{
		var (border, _) = LaidOutBorder();
		border.MeasureDirty.ShouldBeFalse(); // clean after layout

		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = 1,
			To = 0,
			Duration = TimeSpan.FromSeconds(1),
			Apply = v => border.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
			Release = () => border.ClearAnimatedValue(UIElement.OpacityProperty),
		}));

		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.5f, 0.001f);     // resolved through the animation rung
		border.Visual.Opacity.ShouldBe(0.5f, 0.001f); // pushed to the composition node
		border.MeasureDirty.ShouldBeFalse();        // a Render-only change never relays out
	}

	[Test]
	public void Fill_behaviour_stop_reverts_while_hold_end_keeps_the_value()
	{
		var (border, _) = LaidOutBorder();
		var clock = new AnimationClock();
		var stopAnim = new DoubleAnimation
		{
			From = 1,
			To = 0,
			Duration = TimeSpan.FromSeconds(1),
			FillBehavior = FillBehavior.Stop,
			Apply = v => border.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
			Release = () => border.ClearAnimatedValue(UIElement.OpacityProperty),
		};
		clock.Begin(StoryboardOf(stopAnim));
		clock.Advance(1.5); // past the end
		border.Opacity.ShouldBe(1f); // Stop released the rung → reverts to the default

		var holdAnim = new DoubleAnimation
		{
			From = 1,
			To = 0,
			Duration = TimeSpan.FromSeconds(1),
			FillBehavior = FillBehavior.HoldEnd,
			Apply = v => border.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
			Release = () => border.ClearAnimatedValue(UIElement.OpacityProperty),
		};
		clock.Begin(StoryboardOf(holdAnim));
		clock.Advance(1.5);
		border.Opacity.ShouldBe(0f); // HoldEnd keeps the final value
	}

	[Test]
	public void Translate_animates_on_the_visual_path_without_relayout()
	{
		var (border, _) = LaidOutBorder();
		var translate = new TranslateTransform();
		border.RenderTransform = translate;
		var baseX = border.Visual.Transform.M31;

		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = 0,
			To = 100,
			Duration = TimeSpan.FromSeconds(1),
			Target = border,
			TargetProperty = new PropertyPath(UIElement.RenderTransformProperty, TranslateTransform.XProperty),
		}));

		clock.Advance(0.5);
		translate.X.ShouldBe(50f, 0.001f);
		border.Visual.Transform.M31.ShouldBe(baseX + 50f, 0.001f);
		border.MeasureDirty.ShouldBeFalse();
		border.ArrangeDirty.ShouldBeFalse();
	}

	[Test]
	public void Brush_animates_on_the_visual_path()
	{
		var (border, _) = LaidOutBorder();
		var content = border.Visual.Content.ShouldBeOfType<RectContent>();
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new ColorAnimation
		{
			From = new HdrColor(0, 0, 0, 1),
			To = new HdrColor(1, 1, 1, 1),
			Duration = TimeSpan.FromSeconds(1),
			Target = border,
			TargetProperty = new PropertyPath(Border.BackgroundProperty, SolidColorBrush.ColorProperty),
		}));

		border.Visual.IsDirty = false;
		clock.Advance(0.5);
		border.Visual.Content.ShouldBeSameAs(content); // the same content, its paint recoloured in place
		content.Fill.SolidColor().ShouldBe(new HdrColor(0.5f, 0.5f, 0.5f, 1f));
		border.Visual.IsDirty.ShouldBeTrue();
		border.MeasureDirty.ShouldBeFalse();
		border.ArrangeDirty.ShouldBeFalse();
	}

	[Test]
	public void Host_tick_advances_the_clock_before_layout()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var border = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(10, 10, 10)), Width = 20, Height = 20 };
		view.Root.Content = border;

		host.Clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = 1,
			To = 0,
			Duration = TimeSpan.FromSeconds(1),
			Apply = v => border.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
		}));

		host.Tick(0.5, 0f, new Vector2(100, 100));
		border.Opacity.ShouldBe(0.5f, 0.001f);
	}
}
