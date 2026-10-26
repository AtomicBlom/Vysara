using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
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

	[Test]
	public void A_dependency_property_tween_writes_its_local_value()
	{
		var border = new Border();
		var clock = new AnimationClock();
		clock.Tween(border, UIElement.OpacityProperty, 0f, OneSecond);

		clock.Advance(0.25);
		border.Opacity.ShouldBe(0.75f);
		border.HasLocalValue(UIElement.OpacityProperty).ShouldBeTrue();
		clock.Advance(1.0);
		border.Opacity.ShouldBe(0f);
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
