using System.Numerics;
using System.Runtime.CompilerServices;
using Vysara.Animation;
using Vysara.Composition;
using Vysara.Controls;
using Vysara.Drawing;
using Vysara.Media;

namespace Vysara.Tests;

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
	[Arguments(true)]
	[Arguments(false)]
	public void Stopping_a_storyboard_that_finished_holding_its_value_reverts_the_target(bool bound)
	{
		var (border, _) = LaidOutBorder();
		var clock = new AnimationClock();
		var storyboard = StoryboardOf(OpacityTo(border, 0.25, bound));
		clock.Begin(storyboard);
		clock.Advance(1.5); // past the end: finished, holding its last value
		border.Opacity.ShouldBe(0.25f);
		clock.HasActiveClocks.ShouldBeFalse(); // nothing left to advance

		clock.Stop(storyboard);

		border.Opacity.ShouldBe(1f); // released, as WinUI's Storyboard.Stop releases it: back to the default
	}

	[Test]
	public void An_animation_takes_its_property_from_one_still_running_there()
	{
		var (border, _) = LaidOutBorder();
		border.Opacity = 0.6f;
		var clock = new AnimationClock();
		var fadeOut = StoryboardOf(OpacityTo(border, 0.0, bound: true));
		var fadeIn = StoryboardOf(OpacityTo(border, 1.0, bound: true));
		clock.Begin(fadeOut);
		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.3f, 0.001f);

		clock.Begin(fadeIn); // starts where the fade out is, and takes Opacity from it
		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.65f, 0.001f); // the fade in's value, though the fade out is still running

		clock.Stop(fadeOut);
		border.Opacity.ShouldBe(0.65f, 0.001f); // Opacity is the fade in's now: stopping the other leaves it alone
		clock.Advance(0.5);
		border.Opacity.ShouldBe(1f, 0.001f);
		clock.Stop(fadeIn);
		border.Opacity.ShouldBe(0.6f); // back to its own value
	}

	[Test]
	public void A_storyboard_holding_its_value_does_not_keep_its_target_alive()
	{
		var clock = new AnimationClock();
		var target = FinishHolding(clock);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		target.IsAlive.ShouldBeFalse();
	}

	// Run a storyboard on a border nothing else holds to its end, holding the value, and hand back only a weak reference.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference FinishHolding(AnimationClock clock)
	{
		var border = new Border();
		clock.Begin(StoryboardOf(OpacityTo(border, 0.5, bound: true)));
		clock.Advance(1.5);
		border.Opacity.ShouldBe(0.5f);
		return new WeakReference(border);
	}

	// A one-second fade of the border's Opacity to `to` from wherever it is, holding the end: bound through Target and
	// TargetProperty, as generated XAML binds one, or through delegates set in code.
	private static DoubleAnimation OpacityTo(Border border, double to, bool bound) => bound
		? new DoubleAnimation { To = to, Duration = TimeSpan.FromSeconds(1), Target = border, TargetProperty = UIElement.OpacityProperty }
		: new DoubleAnimation
		{
			To = to,
			Duration = TimeSpan.FromSeconds(1),
			Apply = v => border.SetAnimatedValue(UIElement.OpacityProperty, (float)v),
			Release = () => border.ClearAnimatedValue(UIElement.OpacityProperty),
			ReadCurrent = () => border.Opacity,
		};

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
