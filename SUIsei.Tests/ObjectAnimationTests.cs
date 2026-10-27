using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Extensions;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="ObjectAnimationUsingKeyFrames"/>: discrete keys set a property of any type at their times, holding the value
/// it showed before the first; the visual state that collapses what it fades out, released on leaving it whether its
/// storyboard is running or done, and snapped to its last key with transitions off; values the property cannot hold fail
/// as the animation begins; and running and switching states allocates nothing once warm.
/// </summary>
public sealed class ObjectAnimationTests
{
	private static readonly Vector2 Surface = new(200f, 100f);
	private static readonly SolidColorBrush Marker = new(HdrColor.FromRgb(0, 0, 255));

	// An animation of `property` on `target` through `keys` (seconds, value), lasting until its last key as the generator
	// makes it.
	private static ObjectAnimationUsingKeyFrames Keys(DependencyObject target, PropertyPath property, params (double Seconds, object? Value)[] keys)
	{
		var animation = new ObjectAnimationUsingKeyFrames
		{
			Target = target,
			TargetProperty = property,
			Duration = TimeSpan.FromSeconds(keys.Max(static k => k.Seconds)),
		};
		foreach (var (seconds, value) in keys)
			animation.KeyFrames.Add(new DiscreteObjectKeyFrame { KeyTime = TimeSpan.FromSeconds(seconds), Value = value });
		return animation;
	}

	private static Storyboard StoryboardOf(params Timeline[] animations)
	{
		var storyboard = new Storyboard();
		foreach (var animation in animations)
			storyboard.Children.Add(animation);
		return storyboard;
	}

	private static DoubleAnimation FadeTo(UIElement target, double to) => new()
	{
		To = to,
		Duration = TimeSpan.FromSeconds(0.25),
		EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
		Target = target,
		TargetProperty = UIElement.OpacityProperty,
	};

	// WinUI's pattern: Hidden fades the panel out over a quarter second and collapses it as the fade ends; Shown makes it
	// visible at once and fades it in; Default animates nothing. Attached to the control itself, which has no template.
	private static void AddShowingStates(ContentControl control, UIElement panel)
	{
		var group = new VisualStateGroup { Name = "Showing" };
		group.States.Add(new VisualState { Name = "Default" });
		group.States.Add(new VisualState
		{
			Name = "Hidden",
			Storyboard = StoryboardOf(FadeTo(panel, 0.0), Keys(panel, UIElement.VisibilityProperty, (0.25, Visibility.Collapsed))),
		});
		group.States.Add(new VisualState
		{
			Name = "Shown",
			Storyboard = StoryboardOf(Keys(panel, UIElement.VisibilityProperty, (0.0, Visibility.Visible)), FadeTo(panel, 1.0)),
		});
		group.States.Add(new VisualState
		{
			Name = "Marked",
			Storyboard = StoryboardOf(Keys(panel, Border.BackgroundProperty, (0.0, Marker)), Keys(panel, Grid.RowProperty, (0.0, 2))),
		});
		VisualStateManager.GetVisualStateGroups(control).Add(group);
	}

	private static (UIHost Host, ContentControl Control, Border Panel) Hosted()
	{
		var panel = new Border { Width = 40f, Height = 20f, Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) };
		var control = new ContentControl { Content = panel };
		AddShowingStates(control, panel);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = control;
		host.Tick(0, 0f, Surface);
		return (host, control, panel);
	}

	private static double SineInOut(double t) => (1.0 - Math.Cos(Math.PI * t)) / 2.0;

	[Test]
	public void A_discrete_key_sets_its_value_at_its_time()
	{
		var clock = new AnimationClock();
		var border = new Border();
		var storyboard = StoryboardOf(Keys(border, UIElement.VisibilityProperty, (0.25, Visibility.Collapsed)));

		clock.Begin(storyboard);
		clock.Advance(0.2);
		border.Visibility.ShouldBe(Visibility.Visible);
		clock.Advance(0.05);
		border.Visibility.ShouldBe(Visibility.Collapsed);

		clock.Stop(storyboard);
		border.Visibility.ShouldBe(Visibility.Visible); // the animation rung released; the local value was never touched
	}

	[Test]
	public void Before_its_first_key_it_holds_the_value_shown_when_it_began()
	{
		var clock = new AnimationClock();
		var border = new Border();
		var collapsing = Keys(border, UIElement.VisibilityProperty, (0.0, Visibility.Collapsed));
		collapsing.Duration = TimeSpan.FromSeconds(1); // still running, so stopping it releases what it holds
		var collapse = StoryboardOf(collapsing);
		var reveal = StoryboardOf(Keys(border, UIElement.VisibilityProperty, (0.5, Visibility.Visible)));
		clock.Begin(collapse);
		clock.Advance(0.1);

		// The reveal takes over from the collapse, as a visual state change does: begun first, then the other stopped.
		clock.Begin(reveal);
		clock.Stop(collapse);
		clock.Advance(0.25);
		border.Visibility.ShouldBe(Visibility.Collapsed); // still what it showed when the reveal began
		clock.Advance(0.25);
		border.Visibility.ShouldBe(Visibility.Visible);
	}

	[Test]
	public void A_later_animation_takes_over_a_finished_ones_property()
	{
		var clock = new AnimationClock();
		var border = new Border();
		var other = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0));
		var showMarker = StoryboardOf(Keys(border, Border.BackgroundProperty, (0.0, Marker)));
		var showOther = StoryboardOf(Keys(border, Border.BackgroundProperty, (0.0, Marker), (0.5, other)));
		clock.Begin(showMarker);
		clock.Advance(0);
		border.Background.ShouldBeSameAs(Marker); // finished, holding it

		// As a visual state change does: the next begun, then the finished one stopped, which leaves the background alone
		// now that the next holds it.
		clock.Begin(showOther);
		clock.Stop(showMarker);
		border.Background.ShouldBeSameAs(Marker);
		clock.Advance(0.5);
		border.Background.ShouldBeSameAs(other);

		clock.Stop(showOther);
		border.Background.ShouldBeNull();
	}

	[Test]
	public void Keys_need_not_be_in_order_and_the_last_of_equal_times_wins()
	{
		var clock = new AnimationClock();
		var text = new TextBlock { Text = "start" };
		clock.Begin(StoryboardOf(Keys(text, TextBlock.TextProperty, (0.5, "c"), (0.0, "a"), (0.25, "b"), (0.25, "b2"))));

		clock.Advance(0);
		text.Text.ShouldBe("a");
		clock.Advance(0.25);
		text.Text.ShouldBe("b2");
		clock.Advance(0.25);
		text.Text.ShouldBe("c");
	}

	[Test]
	public void It_sets_properties_no_animation_can_interpolate()
	{
		Grid.RowProperty.IsAnimatable.ShouldBeFalse();
		Border.BackgroundProperty.IsAnimatable.ShouldBeFalse();
		var clock = new AnimationClock();
		var border = new Border();
		var storyboard = StoryboardOf(Keys(border, Grid.RowProperty, (0.0, 2)), Keys(border, Border.BackgroundProperty, (0.0, Marker)));

		clock.Begin(storyboard);
		clock.Advance(0);
		Grid.GetRow(border).ShouldBe(2);
		border.Background.ShouldBeSameAs(Marker);

		clock.Stop(storyboard);
		Grid.GetRow(border).ShouldBe(0);
		border.Background.ShouldBeNull();
	}

	[Test]
	public void Fill_behaviour_stop_releases_the_last_key_when_it_ends()
	{
		var clock = new AnimationClock();
		var border = new Border();
		var animation = Keys(border, UIElement.VisibilityProperty, (0.0, Visibility.Collapsed));
		animation.Duration = TimeSpan.FromSeconds(0.5);
		animation.FillBehavior = FillBehavior.Stop;
		clock.Begin(StoryboardOf(animation));

		clock.Advance(0.25);
		border.Visibility.ShouldBe(Visibility.Collapsed);
		clock.Advance(0.25);
		border.Visibility.ShouldBe(Visibility.Visible);
		clock.HasActiveClocks.ShouldBeFalse();
	}

	[Test]
	public void A_value_the_property_cannot_hold_fails_when_it_begins()
	{
		var clock = new AnimationClock();
		var border = new Border();

		Should.Throw<InvalidOperationException>(() => clock.Begin(StoryboardOf(Keys(border, UIElement.OpacityProperty, (0.0, 0.5)))))
			.Message.ShouldBe("An ObjectAnimationUsingKeyFrames cannot set Opacity, which holds a Single, to a Double (its key at 00:00:00).");
		Should.Throw<InvalidOperationException>(() => clock.Begin(StoryboardOf(Keys(border, UIElement.VisibilityProperty, (0.25, null)))))
			.Message.ShouldBe("An ObjectAnimationUsingKeyFrames cannot set Visibility, which holds a Visibility, to null (its key at 00:00:00.2500000).");
		border.Visibility.ShouldBe(Visibility.Visible);

		// Null is a value where the property takes one.
		var storyboard = StoryboardOf(Keys(new Border { Background = Marker }, Border.BackgroundProperty, (0.0, null)));
		Should.NotThrow(() => clock.Begin(storyboard));
	}

	[Test]
	public void A_value_the_property_refuses_fails_when_it_begins()
	{
		var clock = new AnimationClock();
		var text = new TextBlock { Text = "Outlined" };

		Should.Throw<ArgumentOutOfRangeException>(() => clock.Begin(StoryboardOf(Keys(text, TextEffects.OutlineThicknessProperty, (0.5, -1f)))));
		TextEffects.GetOutlineThickness(text).ShouldBe(0f);
	}

	[Test]
	public void A_state_collapses_what_it_fades_out_and_leaving_it_releases_both()
	{
		var (host, control, panel) = Hosted();

		VisualStateManager.GoToState(control, "Hidden", useTransitions: true).ShouldBeTrue();
		host.Tick(0.1, 0f, Surface);
		panel.Opacity.ShouldBe((float)(1.0 - SineInOut(0.4)), 1e-6f);
		panel.Visibility.ShouldBe(Visibility.Visible); // until the fade ends
		host.Tick(0.15, 0f, Surface);
		panel.Opacity.ShouldBe(0f);
		panel.Visibility.ShouldBe(Visibility.Collapsed);
		panel.LayoutRect.Width.ShouldBe(0f); // collapsed takes no space
		host.Tick(1, 0f, Surface);           // long finished, holding its end values
		host.Clock.HasActiveClocks.ShouldBeFalse();

		// Leaving for a state that animates neither property hands both back, though the storyboard had finished.
		VisualStateManager.GoToState(control, "Default", useTransitions: true).ShouldBeTrue();
		panel.Visibility.ShouldBe(Visibility.Visible);
		panel.Opacity.ShouldBe(1f);
		host.Tick(0, 0f, Surface);
		panel.LayoutRect.Width.ShouldBe(40f);
	}

	[Test]
	public void Shown_makes_it_visible_at_once_and_fades_in_from_where_the_fade_out_left_it()
	{
		var (host, control, panel) = Hosted();
		VisualStateManager.GoToState(control, "Hidden", useTransitions: true);
		host.Tick(0.5, 0f, Surface);
		panel.Visibility.ShouldBe(Visibility.Collapsed);

		VisualStateManager.GoToState(control, "Shown", useTransitions: true);
		host.Tick(0, 0f, Surface);
		panel.Visibility.ShouldBe(Visibility.Visible);
		panel.Opacity.ShouldBe(0f);
		host.Tick(0.125, 0f, Surface);
		panel.Opacity.ShouldBe(0.5f, 1e-6f);

		// Hidden again midway: the fade out starts from the opacity on screen, and the panel stays visible until it ends.
		VisualStateManager.GoToState(control, "Hidden", useTransitions: true);
		host.Tick(0.125, 0f, Surface);
		panel.Opacity.ShouldBe((float)(0.5 * (1.0 - SineInOut(0.5))), 1e-6f);
		panel.Visibility.ShouldBe(Visibility.Visible);
		host.Tick(0.125, 0f, Surface);
		panel.Visibility.ShouldBe(Visibility.Collapsed);
	}

	[Test]
	public void With_transitions_off_a_state_snaps_to_its_last_key_and_leaving_it_releases()
	{
		var (_, control, panel) = Hosted();

		VisualStateManager.GoToState(control, "Hidden", useTransitions: false).ShouldBeTrue();
		panel.Visibility.ShouldBe(Visibility.Collapsed);
		panel.Opacity.ShouldBe(0f);

		VisualStateManager.GoToState(control, "Marked", useTransitions: false).ShouldBeTrue();
		panel.Visibility.ShouldBe(Visibility.Visible);
		panel.Opacity.ShouldBe(1f);
		panel.Background.ShouldBeSameAs(Marker);
		Grid.GetRow(panel).ShouldBe(2);

		VisualStateManager.GoToState(control, "Default", useTransitions: false).ShouldBeTrue();
		panel.Background.ShouldNotBeSameAs(Marker);
		Grid.GetRow(panel).ShouldBe(0);
	}

	[Test]
	public void A_detached_control_snaps_to_the_last_key_and_releases_it_on_leaving()
	{
		var panel = new Border();
		var control = new ContentControl { Content = panel }; // never attached: no clock
		AddShowingStates(control, panel);

		VisualStateManager.GoToState(control, "Hidden", useTransitions: true).ShouldBeTrue();
		panel.Visibility.ShouldBe(Visibility.Collapsed);
		VisualStateManager.GoToState(control, "Default", useTransitions: true).ShouldBeTrue();
		panel.Visibility.ShouldBe(Visibility.Visible);
	}

	[Test]
	public void A_compiled_view_collapses_what_it_fades_out_and_sets_brushes_and_text()
	{
		var view = new Sample.AnimationView();
		view.InitializeComponent();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0, 0f, Surface);
		var ownBackground = view.Panel.Background;

		VisualStateManager.GoToState(view.Fader, "Hidden", useTransitions: true).ShouldBeTrue();
		host.Tick(0.1, 0f, Surface);
		view.Panel.Opacity.ShouldBe((float)(1.0 - SineInOut(0.4)), 1e-6f);
		view.Panel.Visibility.ShouldBe(Visibility.Visible);
		host.Tick(0.15, 0f, Surface);
		view.Panel.Opacity.ShouldBe(0f);
		view.Panel.Visibility.ShouldBe(Visibility.Collapsed);

		VisualStateManager.GoToState(view.Fader, "Shown", useTransitions: true).ShouldBeTrue();
		host.Tick(0, 0f, Surface);
		view.Panel.Visibility.ShouldBe(Visibility.Visible);
		host.Tick(0.125, 0f, Surface);
		view.Panel.Opacity.ShouldBe(0.5f, 1e-6f);

		// The other group: a resource's brush at once, then an object's, and text a quarter second in.
		VisualStateManager.GoToState(view.Fader, "Marked", useTransitions: true).ShouldBeTrue();
		host.Tick(0, 0f, Surface);
		view.Panel.Background.ShouldBeSameAs(view.Fader.FindResource("Mark"));
		view.Caption.Text.ShouldBe("Plain");
		host.Tick(0.25, 0f, Surface);
		view.Caption.Text.ShouldBe("Marked");
		host.Tick(0.25, 0f, Surface);
		view.Panel.Background.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(HdrColor.FromRgb(255, 255, 0));
		view.Panel.ShownColor().ShouldBe(HdrColor.FromRgb(255, 255, 0));

		// Leaving each state hands its properties back.
		VisualStateManager.GoToState(view.Fader, "Unmarked", useTransitions: true).ShouldBeTrue();
		VisualStateManager.GoToState(view.Fader, "Default", useTransitions: true).ShouldBeTrue();
		view.Panel.Background.ShouldBeSameAs(ownBackground);
		view.Caption.Text.ShouldBe("Plain");
		view.Panel.Visibility.ShouldBe(Visibility.Visible);
		view.Panel.Opacity.ShouldBe(1f);

		// With transitions off, the state shows its last keys at once: the object's brush, not the resource's.
		VisualStateManager.GoToState(view.Fader, "Marked", useTransitions: false).ShouldBeTrue();
		view.Panel.Background.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(HdrColor.FromRgb(255, 255, 0));
		view.Caption.Text.ShouldBe("Marked");
		VisualStateManager.GoToState(view.Fader, "Unmarked", useTransitions: false).ShouldBeTrue();
		view.Panel.Background.ShouldBeSameAs(ownBackground);
		view.Caption.Text.ShouldBe("Plain");
	}

	[Test]
	public void Running_and_switching_states_allocates_nothing_once_warm()
	{
		var (host, control, panel) = Hosted();

		// Beside the states, every ease drives an element's opacity, back and forth for ever.
		IEasingFunction[] eases =
		[
			new SineEase(), new QuadraticEase(), new CubicEase(), new QuarticEase(), new QuinticEase(), new CircleEase(),
			new PowerEase { Power = 3.5 }, new ExponentialEase { Exponent = 6 }, new BackEase(), new ElasticEase(), new BounceEase(),
		];
		var eased = new StackPanel();
		var easing = new Storyboard();
		foreach (var ease in eases)
		{
			var element = new Border { Width = 10f, Height = 4f, Background = Marker };
			eased.Children.Add(element);
			easing.Children.Add(new DoubleAnimation
			{
				To = 0.0,
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = ease,
				Target = element,
				TargetProperty = UIElement.OpacityProperty,
			});
		}
		host.CreateRoot(UILayer.Default).Root.Content = eased;
		host.Clock.Begin(easing);

		for (var i = 0; i < 3; i++)
			Cycle();
		Allocations.ShouldAllocateNothing(Cycle);
		panel.Visibility.ShouldBe(Visibility.Visible);

		// Hide and show, mark and leave, with frames between.
		void Cycle()
		{
			Go("Hidden", 20);
			Go("Shown", 20);
			Go("Marked", 2);
			Go("Default", 2);
		}

		void Go(string state, int frames)
		{
			VisualStateManager.GoToState(control, state, useTransitions: true);
			for (var f = 0; f < frames; f++)
				host.Tick(1.0 / 60.0, 0f, Surface);
		}
	}
}
