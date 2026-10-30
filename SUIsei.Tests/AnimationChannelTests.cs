using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Extensions;
using SUIsei.Media;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// How animations reach their targets: a <see cref="PropertyPath"/> resolved to a dependency property, through the objects
/// that hold it, and written unboxed, WinUI's start-from-the-current-value rule when <c>From</c> is omitted, binding
/// failures that throw rather than animate nothing, the code-first <see cref="AnimationClock.Tween{TTarget, T}"/>, and a
/// storyboard compiled from XAML driving each kind of path.
/// </summary>
public sealed class AnimationChannelTests
{
	private static readonly HdrColor Red = new(1f, 0f, 0f, 1f);
	private static readonly HdrColor Blue = new(0f, 0f, 1f, 1f);
	private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

	private static Storyboard StoryboardOf(params Timeline[] animations)
	{
		var storyboard = new Storyboard();
		foreach (var animation in animations)
			storyboard.Children.Add(animation);
		return storyboard;
	}

	[Test]
	public void An_animation_with_no_From_starts_at_the_property_value()
	{
		var border = new Border { Opacity = 0.8f };
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation { To = 0.0, Duration = OneSecond, Target = border, TargetProperty = UIElement.OpacityProperty }));

		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.4f, 1e-6f);
	}

	[Test]
	public void A_colour_animation_with_no_From_starts_at_the_brush_colour()
	{
		var border = new Border { Background = new SolidColorBrush(Red) };
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new ColorAnimation { To = Blue, Duration = OneSecond, Target = border, TargetProperty = BackgroundColor }));

		clock.Advance(0.5);
		border.Background.SolidColor().ShouldBe(new HdrColor(0.5f, 0f, 0.5f, 1f));
	}

	/// <summary>
	/// The path is walked again each time the storyboard begins: a background replaced since (by a style, a binding or
	/// code) is the brush animated, and the old brush, which may be shared with other elements, is let go of the colour
	/// the earlier run held on it.
	/// </summary>
	[Test]
	public void A_storyboard_animates_the_brush_its_path_reaches_now_and_lets_go_of_a_replaced_one()
	{
		var first = new SolidColorBrush(Red);
		var border = new Border { Background = first };
		var clock = new AnimationClock();
		var storyboard = StoryboardOf(new ColorAnimation { To = Blue, Duration = OneSecond, Target = border, TargetProperty = BackgroundColor });
		clock.Begin(storyboard);
		clock.Advance(1.5);
		first.Color.ShouldBe(Blue); // finished, and holding

		var second = new SolidColorBrush(Red);
		border.Background = second;
		clock.Begin(storyboard);
		clock.Advance(1.5);

		second.Color.ShouldBe(Blue);
		first.Color.ShouldBe(Red); // released

		clock.Stop(storyboard);
		second.Color.ShouldBe(Red);
	}

	/// <summary>Restarted while it runs, the storyboard releases the old brush through the binding it ran on.</summary>
	[Test]
	public void Restarting_after_the_brush_is_replaced_mid_run_releases_the_old_one()
	{
		var first = new SolidColorBrush(Red);
		var border = new Border { Background = first };
		var clock = new AnimationClock();
		var storyboard = StoryboardOf(new ColorAnimation { To = Blue, Duration = OneSecond, Target = border, TargetProperty = BackgroundColor });
		clock.Begin(storyboard);
		clock.Advance(0.5);

		var second = new SolidColorBrush(Red);
		border.Background = second;
		clock.Begin(storyboard);

		first.Color.ShouldBe(Red);
		clock.Advance(1.5);
		second.Color.ShouldBe(Blue);
	}

	// WinUI's (Border.Background).(SolidColorBrush.Color), and the same through Control's background.
	private static readonly PropertyPath BackgroundColor = new(Border.BackgroundProperty, SolidColorBrush.ColorProperty);
	private static readonly PropertyPath ControlBackgroundColor = new(Control.BackgroundProperty, SolidColorBrush.ColorProperty);

	[Test]
	public void A_path_step_into_a_brush_of_another_kind_fails_when_it_begins()
	{
		var border = new Border { Background = new LinearGradientBrush() };
		var storyboard = StoryboardOf(new ColorAnimation { To = Blue, Target = border, TargetProperty = BackgroundColor });

		Should.Throw<InvalidOperationException>(() => new AnimationClock().Begin(storyboard)).Message
			.ShouldContain("Background holds a LinearGradientBrush, which has no Color");
	}

	[Test]
	public void A_storyboard_that_takes_over_a_property_starts_where_the_other_left_it()
	{
		var border = new Border();
		var clock = new AnimationClock();
		var fadeOut = StoryboardOf(new DoubleAnimation { To = 0.0, Duration = OneSecond, Target = border, TargetProperty = UIElement.OpacityProperty });
		var fadeIn = StoryboardOf(new DoubleAnimation { To = 1.0, Duration = OneSecond, Target = border, TargetProperty = UIElement.OpacityProperty });

		clock.Begin(fadeOut);
		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.5f, 1e-6f);

		clock.Begin(fadeIn); // starts at the 0.5 on screen, not at the property's own 1
		clock.Stop(fadeOut);
		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.75f, 1e-6f);
	}

	[Test]
	public void A_visual_state_change_mid_transition_hands_over_from_the_colour_on_screen()
	{
		var control = new ContentControl { Background = new SolidColorBrush(HdrColor.FromRgb(255, 255, 255)) };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = control;
		host.Tick(0, 0f, new Vector2(100f, 100f));
		VisualStateManager.SetVisualStateGroups(control,
		[
			new VisualStateGroup
			{
				Name = "Common",
				States =
				{
					new VisualState { Name = "Red", Storyboard = StoryboardOf(new ColorAnimation { To = Red, Duration = OneSecond, Target = control, TargetProperty = ControlBackgroundColor }) },
					new VisualState { Name = "Blue", Storyboard = StoryboardOf(new ColorAnimation { To = Blue, Duration = OneSecond, Target = control, TargetProperty = ControlBackgroundColor }) },
				},
			},
		]);

		VisualStateManager.GoToState(control, "Red", useTransitions: true).ShouldBeTrue();
		host.Tick(0.5, 0f, new Vector2(100f, 100f));
		var midway = control.ShownColor(); // halfway from white to red
		midway.ShouldBe(new HdrColor(1f, 0.5f, 0.5f, 1f));

		VisualStateManager.GoToState(control, "Blue", useTransitions: true).ShouldBeTrue();
		host.Tick(0.5, 0f, new Vector2(100f, 100f));
		control.ShownColor().ShouldBe(Lerps.Color(midway, Blue, 0.5f)); // from the midway colour, with no jump
	}

	[Test]
	public void A_path_steps_through_a_property_that_holds_an_object()
	{
		var leaf = new Leaf();
		var holder = new Holder();
		holder.SetValue(Holder.LeafProperty, leaf);
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = 0.0,
			To = 10.0,
			Duration = OneSecond,
			Target = holder,
			TargetProperty = new PropertyPath(Holder.LeafProperty, Leaf.ValueProperty),
		}));

		clock.Advance(0.5);
		leaf.GetValue(Leaf.ValueProperty).ShouldBe(5f);
	}

	[Test]
	public void A_path_through_a_property_that_holds_no_object_fails_when_it_begins()
	{
		var clock = new AnimationClock();
		var storyboard = StoryboardOf(new DoubleAnimation
		{
			To = 1.0,
			Target = new Holder(),
			TargetProperty = new PropertyPath(Holder.LeafProperty, Leaf.ValueProperty),
		});

		Should.Throw<InvalidOperationException>(() => clock.Begin(storyboard)).Message.ShouldContain("Leaf holds no object");
	}

	[Test]
	public void An_animation_of_the_wrong_type_fails_when_it_begins()
	{
		var clock = new AnimationClock();
		var storyboard = StoryboardOf(new ColorAnimation { To = Blue, Target = new Border(), TargetProperty = UIElement.OpacityProperty });

		Should.Throw<InvalidOperationException>(() => clock.Begin(storyboard)).Message
			.ShouldBe("A ColorAnimation cannot animate Opacity, which holds a Single.");
	}

	[Test]
	public void A_property_of_a_type_with_no_interpolation_is_not_animatable()
	{
		Grid.RowProperty.IsAnimatable.ShouldBeFalse();
		UIElement.OpacityProperty.IsAnimatable.ShouldBeTrue();
		Canvas.LeftProperty.IsAnimatable.ShouldBeTrue();

		var clock = new AnimationClock();
		var storyboard = StoryboardOf(new DoubleAnimation { To = 1.0, Target = new Border(), TargetProperty = Grid.RowProperty });
		Should.Throw<InvalidOperationException>(() => clock.Begin(storyboard)).Message.ShouldContain("Row is not animatable");
	}

	[Test]
	public void A_tween_eases_a_clr_property_to_its_target_and_leaves_it_there()
	{
		var mover = new Mover { X = 10f };
		var clock = new AnimationClock();
		clock.Tween(mover, static m => m.X, static (m, v) => m.X = v, 20f, OneSecond);

		clock.HasActiveClocks.ShouldBeTrue();
		clock.Advance(0.5);
		mover.X.ShouldBe(15f);
		clock.Advance(0.5);
		mover.X.ShouldBe(20f);
		clock.HasActiveClocks.ShouldBeFalse();
		clock.Advance(0.5);
		mover.X.ShouldBe(20f);
	}

	[Test]
	public void A_tween_on_a_property_already_tweening_takes_over_where_it_is()
	{
		// Takeover is keyed by target and setter delegate; a static lambda at one call site is one delegate.
		Func<Mover, Vector2> get = static m => m.Position;
		Action<Mover, Vector2> set = static (m, v) => m.Position = v;
		var mover = new Mover();
		var clock = new AnimationClock();
		clock.Tween(mover, get, set, new Vector2(100f, 0f), OneSecond);
		clock.Advance(0.5);
		mover.Position.ShouldBe(new Vector2(50f, 0f));

		clock.Tween(mover, get, set, Vector2.Zero, OneSecond, new QuadraticEase { EasingMode = EasingMode.EaseIn });
		clock.Advance(0.5);
		mover.Position.ShouldBe(new Vector2(37.5f, 0f)); // 50 → 0, eased in to a quarter of the way
		clock.Advance(0.5);
		mover.Position.ShouldBe(Vector2.Zero);
		clock.HasActiveClocks.ShouldBeFalse(); // the first tween was replaced, not left running
	}

	/// <summary>As a WinUI property transition: the end becomes the local value at once, and only what is shown moves
	/// toward it, on the transition rung above the local value, cleared when the tween ends.</summary>
	[Test]
	public void A_dependency_property_tween_sets_its_end_as_the_local_value_at_once_and_shows_the_way_there()
	{
		var border = new Border();
		var clock = new AnimationClock();
		clock.Tween(border, UIElement.OpacityProperty, 0f, OneSecond);

		border.HasLocalValue(UIElement.OpacityProperty).ShouldBeTrue();
		border.TopRung(UIElement.OpacityProperty).ShouldBe(DependencyObject.ValueRung.Transition);
		border.Opacity.ShouldBe(1f); // nothing moves until the first frame
		clock.Advance(0.25);
		border.Opacity.ShouldBe(0.75f);
		clock.Advance(1.0);
		border.Opacity.ShouldBe(0f);
		border.TopRung(UIElement.OpacityProperty).ShouldBe(DependencyObject.ValueRung.Local);
		clock.HasActiveClocks.ShouldBeFalse();
	}

	/// <summary>
	/// An outline can't be negative, and a BackEase overshoots its end: tweening to 0 passes through negative values. Only
	/// the end is checked, as the tween starts, so the tween runs to its end without throwing.
	/// </summary>
	[Test]
	public void A_tween_that_overshoots_the_valid_range_never_throws_mid_tween()
	{
		var text = new TextBlock { Text = "Hi" };
		TextEffects.SetOutlineThickness(text, 0.05f);
		var clock = new AnimationClock();
		clock.Tween(text, TextEffects.OutlineThicknessProperty, 0f, OneSecond, new BackEase { EasingMode = EasingMode.EaseOut });

		var lowest = float.MaxValue;
		for (var i = 0; i < 60; i++)
		{
			clock.Advance(1.0 / 60);
			lowest = MathF.Min(lowest, TextEffects.GetOutlineThickness(text));
		}

		lowest.ShouldBeLessThan(0f); // the overshoot happened
		TextEffects.GetOutlineThickness(text).ShouldBe(0f);
		Should.Throw<ArgumentException>(() => clock.Tween(text, TextEffects.OutlineThicknessProperty, -1f, OneSecond));
	}

	/// <summary>
	/// Stopped mid-overshoot, the property shows the local value the tween set, as a WinUI transition cut
	/// short lands on the value that was set.
	/// </summary>
	[Test]
	public void Stopping_a_tween_mid_overshoot_leaves_its_end_value()
	{
		var text = new TextBlock { Text = "Hi" };
		TextEffects.SetOutlineThickness(text, 0.05f);
		var clock = new AnimationClock();
		clock.Tween(text, TextEffects.OutlineThicknessProperty, 0f, OneSecond, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 2 });
		while (TextEffects.GetOutlineThickness(text) >= 0f)
			clock.Advance(1.0 / 60);

		clock.StopTweens(text);

		TextEffects.GetOutlineThickness(text).ShouldBe(0f);
		clock.HasActiveClocks.ShouldBeFalse();
	}

	/// <summary>The end is the local value from the start: a tween stopped before its first frame shows it, never the
	/// style's value it started from.</summary>
	[Test]
	public void Stopping_a_tween_before_its_first_frame_shows_its_end_not_the_style_s_value()
	{
		var border = new Border
		{
			Style = new Style { Setters = { new Setter { Property = UIElement.OpacityProperty, Value = 0.4f } } },
		};
		var clock = new AnimationClock();
		clock.Tween(border, UIElement.OpacityProperty, 0.9f, OneSecond);

		clock.StopTweens(border);

		border.Opacity.ShouldBe(0.9f);
		border.TopRung(UIElement.OpacityProperty).ShouldBe(DependencyObject.ValueRung.Local);
		border.Style = null;
		border.Opacity.ShouldBe(0.9f); // the tween's end, set locally, outranks the style as a set value does
	}

	/// <summary>A value set while a tween runs ends it and shows at once, as a set without a transition does; a tween
	/// started on a tweening property takes over from the value on screen.</summary>
	[Test]
	public void A_local_set_mid_tween_ends_it_and_another_tween_takes_over_from_the_screen()
	{
		var border = new Border();
		var clock = new AnimationClock();
		clock.Tween(border, UIElement.OpacityProperty, 0f, OneSecond);
		clock.Advance(0.25);

		border.Opacity = 0.6f;
		border.Opacity.ShouldBe(0.6f);
		clock.Advance(0.25);
		border.Opacity.ShouldBe(0.6f);
		clock.HasActiveClocks.ShouldBeFalse();

		clock.Tween(border, UIElement.OpacityProperty, 0f, OneSecond);
		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.3f, 1e-6f);
		clock.Tween(border, UIElement.OpacityProperty, 1f, OneSecond);
		border.Opacity.ShouldBe(0.3f, 1e-6f); // from the value on screen
		clock.Advance(0.5);
		border.Opacity.ShouldBe(0.65f, 1e-6f);
	}

	/// <summary>A storyboard holding the property (a visual state) outranks a tween as it outranks a local value: the state's
	/// value shows throughout the tween and after it, and the tween's end shows when the state releases it.</summary>
	[Test]
	public void A_held_storyboard_value_outranks_a_tween_and_the_tween_s_end_shows_when_it_is_released()
	{
		var border = new Border { Opacity = 0.2f };
		var clock = new AnimationClock();
		var state = StoryboardOf(new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromSeconds(0.1), Target = border, TargetProperty = UIElement.OpacityProperty });
		clock.Begin(state);
		clock.Advance(0.2);
		border.Opacity.ShouldBe(0.5f);

		clock.Tween(border, UIElement.OpacityProperty, 1f, OneSecond);
		for (var i = 0; i < 70; i++)
		{
			clock.Advance(1.0 / 60);
			border.Opacity.ShouldBe(0.5f);
		}

		clock.Stop(state);
		border.Opacity.ShouldBe(1f);
	}

	/// <summary>A storyboard beginning mid-tween outranks it at once, and the tween ending never wipes the value the
	/// storyboard holds.</summary>
	[Test]
	public void A_storyboard_begun_mid_tween_outranks_it_and_keeps_its_held_value_when_the_tween_ends()
	{
		var border = new Border();
		var clock = new AnimationClock();
		clock.Tween(border, UIElement.OpacityProperty, 0f, OneSecond);
		clock.Advance(0.25);
		var state = StoryboardOf(new DoubleAnimation { To = 0.5, Duration = TimeSpan.FromSeconds(0.1), Target = border, TargetProperty = UIElement.OpacityProperty });

		clock.Begin(state);
		for (var i = 0; i < 70; i++)
			clock.Advance(1.0 / 60);

		border.Opacity.ShouldBe(0.5f);
		clock.Stop(state);
		border.Opacity.ShouldBe(0f);
	}

	/// <summary>A storyboard with no From starts from what is on screen, which may be another animation's overshoot; that
	/// start isn't checked, so taking over never throws.</summary>
	[Test]
	public void A_storyboard_with_no_From_takes_over_from_an_overshoot_without_throwing()
	{
		var text = new TextBlock { Text = "Hi" };
		TextEffects.SetOutlineThickness(text, 0.05f);
		var clock = new AnimationClock();
		clock.Tween(text, TextEffects.OutlineThicknessProperty, 0f, OneSecond, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 2 });
		while (TextEffects.GetOutlineThickness(text) >= 0f)
			clock.Advance(1.0 / 60);
		var takeOver = new DoubleAnimation { To = 0.1, Duration = OneSecond, Target = text, TargetProperty = TextEffects.OutlineThicknessProperty };

		Should.NotThrow(() => clock.Begin(StoryboardOf(takeOver)));
		for (var i = 0; i < 120; i++)
			clock.Advance(1.0 / 60);
		TextEffects.GetOutlineThickness(text).ShouldBe(0.1f, 1e-6f); // the tween ending leaves the storyboard's held value
		clock.HasActiveClocks.ShouldBeFalse();
	}

	[Test]
	public void Stopping_a_target_s_tweens_leaves_its_properties_where_they_are()
	{
		var mover = new Mover();
		var clock = new AnimationClock();
		clock.Tween(mover, static m => m.X, static (m, v) => m.X = v, 100f, OneSecond);
		clock.Advance(0.5);

		clock.StopTweens(mover);
		clock.HasActiveClocks.ShouldBeFalse();
		clock.Advance(0.5);
		mover.X.ShouldBe(50f);
	}

	[Test]
	public void A_tween_of_a_type_with_no_standard_interpolation_needs_one()
	{
		var label = new TextBlock { Text = "a" };
		var clock = new AnimationClock();
		Should.Throw<ArgumentException>(() => clock.Tween(label, static l => l.Text, static (l, v) => l.Text = v, "abc", OneSecond));

		clock.Tween(label, static l => l.Text, static (l, v) => l.Text = v, "abcdef", OneSecond,
			lerp: static (from, to, t) => to.Substring(0, (int)(from.Length + (to.Length - from.Length) * t)));
		clock.Advance(0.6);
		label.Text.ShouldBe("abcd");
	}

	[Test]
	public void A_compiled_storyboard_drives_attached_keyframed_and_brush_paths()
	{
		var view = new AnimationView();
		view.InitializeComponent();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0, 0f, new Vector2(300f, 200f));

		VisualStateManager.GoToState(view.Host, "Moved", useTransitions: true).ShouldBeTrue();

		host.Tick(0.25, 0f, new Vector2(300f, 200f));
		Canvas.GetLeft(view.Box).ShouldBe(25f, 1e-4f);  // (Canvas.Left), from its own 0
		view.Box.LayoutRect.X.ShouldBe(25f, 1e-4f);     // and re-arranged in the same tick
		view.Box.Opacity.ShouldBe(1f);                  // holding the first key until the discrete one
		view.Box.ShownColor().ShouldBe(Lerps.Color(Red, Blue, 0.25f));

		host.Tick(0.5, 0f, new Vector2(300f, 200f));
		Canvas.GetLeft(view.Box).ShouldBe(75f, 1e-4f);
		view.Box.Opacity.ShouldBe(0.5f + (0.25f - 0.5f) * 0.125f, 1e-6f); // past the discrete key, eased in toward the last
		view.Box.ShownColor().ShouldBe(Lerps.Color(Red, Blue, 0.75f));
	}

	private sealed class Mover
	{
		public float X;

		public Vector2 Position { get; set; }
	}

	private sealed class Holder : DependencyObject
	{
		public static readonly DependencyProperty<Holder, Leaf?> LeafProperty =
			DependencyProperty.Register("Leaf", new PropertyMetadata<Holder, Leaf?>());
	}

	private sealed class Leaf : DependencyObject
	{
		public static readonly DependencyProperty<Leaf, float> ValueProperty =
			DependencyProperty.Register("Value", new PropertyMetadata<Leaf, float>());
	}
}
