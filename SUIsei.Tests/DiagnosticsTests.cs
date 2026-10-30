using System.Numerics;

using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="Diagnostics"/>, the surface a diagnostics tool loaded into a game builds on, against headless hosts. This
/// project turns the switch on in its runtime config, as a game opts in. The host registry and the posted-work queue are
/// process-global, so these run apart from each other and from <see cref="HotReloadTests"/>, which resets the registry.
/// </summary>
[NotInParallel]
public sealed class DiagnosticsTests
{
	private static readonly Vector2 Surface = new(200f, 200f);

	private static (UIHost Host, UIView View) Hosted(UIElement content, float layoutScale = 1f)
	{
		var host = new UIHost();
		var view = host.CreateRoot(new UILayer(0, layoutScale));
		view.Root.Content = content;
		host.Tick(0.016, 0f, Surface);
		return (host, view);
	}

	// A filled box at the top left of whatever holds it.
	private static Border Box(float w = 50f, float h = 50f) => new()
	{
		Width = w,
		Height = h,
		HorizontalAlignment = HorizontalAlignment.Left,
		VerticalAlignment = VerticalAlignment.Top,
		Background = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)),
	};

	private static ControlTemplate OpacityTemplate() => new()
	{
		Factory = control =>
		{
			var part = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(0, 0, 255)) };
			control.RegisterTemplateBinding(UIElement.OpacityProperty, part, UIElement.OpacityProperty);
			return part;
		},
	};

	private static Diagnostics.PropertyValue Find(IReadOnlyList<Diagnostics.PropertyValue> properties, DependencyProperty dp) =>
		properties.Single(p => ReferenceEquals(p.Property, dp));

	[Test]
	public void The_runtime_config_turns_diagnostics_on()
	{
		Diagnostics.Enabled.ShouldBeTrue();
	}

	// --- Hosts and Post ---

	[Test]
	public void Hosts_lists_each_live_host()
	{
		var first = new UIHost();
		var second = new UIHost();

		var hosts = Diagnostics.Hosts;
		hosts.ShouldContain(first);
		hosts.ShouldContain(second);
		hosts.ToList().IndexOf(first).ShouldBeLessThan(hosts.ToList().IndexOf(second));
	}

	[Test]
	public void Posted_work_runs_at_the_next_tick_on_the_ticking_thread()
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default);
		var tickThread = Environment.CurrentManagedThreadId;
		var ranOn = -1;
		using var ran = new ManualResetEventSlim();

		var poster = new Thread(() => Diagnostics.Post(() => { ranOn = Environment.CurrentManagedThreadId; ran.Set(); }));
		poster.Start();
		poster.Join();
		ran.IsSet.ShouldBeFalse(); // posting runs nothing

		host.Tick(0.016, 0f, Surface);
		ran.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
		ranOn.ShouldBe(tickThread);
	}

	[Test]
	public void Work_posted_by_posted_work_waits_for_the_following_tick()
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default);
		var inner = false;
		Diagnostics.Post(() => Diagnostics.Post(() => inner = true));

		host.Tick(0.016, 0f, Surface);
		inner.ShouldBeFalse();
		host.Tick(0.016, 0f, Surface);
		inner.ShouldBeTrue();
	}

	[Test]
	public void Draining_an_empty_queue_allocates_nothing()
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default);
		Diagnostics.Post(() => { });
		Diagnostics.Post(host, () => { });
		host.Tick(0.016, 0f, Surface); // both queues now exist, and are empty
		Diagnostics.RunPosted(host);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++)
			Diagnostics.RunPosted(host);
		(GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
	}

	/// <summary>Work posted to one host waits for that host's tick: another host ticking never runs it.</summary>
	[Test]
	public void Work_posted_to_a_host_runs_only_at_that_hosts_tick()
	{
		var mine = new UIHost();
		mine.CreateRoot(UILayer.Default);
		var other = new UIHost();
		other.CreateRoot(UILayer.Default);
		var ran = false;
		Diagnostics.Post(mine, () => ran = true);

		other.Tick(0.016, 0f, Surface);
		ran.ShouldBeFalse();
		mine.Tick(0.016, 0f, Surface);
		ran.ShouldBeTrue();
	}

	// --- The tree ---

	[Test]
	public void ChildrenOf_and_ParentOf_walk_panels_and_templates()
	{
		var templated = new ContentControl { Template = OpacityTemplate(), Width = 40, Height = 40 };
		var text = new TextBlock { Text = "Hi" };
		var panel = new StackPanel();
		panel.Children.Add(text);
		panel.Children.Add(templated);
		var (_, view) = Hosted(panel);

		Diagnostics.ChildrenOf(view).ShouldBe([panel]);
		Diagnostics.ChildrenOf(panel).ShouldBe([text, templated]);
		var part = Diagnostics.ChildrenOf(templated).ShouldHaveSingleItem();
		part.ShouldBeOfType<Border>();

		Diagnostics.ParentOf(part).ShouldBe(templated);
		Diagnostics.ParentOf(text).ShouldBe(panel);
		Diagnostics.ParentOf(panel).ShouldBeNull();
	}

	[Test]
	public void A_views_top_level_lists_open_popups_after_its_content_and_leaves_overlays_out()
	{
		var content = Box();
		var (_, view) = Hosted(content);
		var popup = Box(10f, 10f);
		var overlay = new Border();

		Diagnostics.AddOverlay(view, overlay);
		view.Root.AddOverlay(popup, Vector2.Zero); // as a Popup opens

		Diagnostics.ChildrenOf(view).ShouldBe([content, popup]);
	}

	[Test]
	public void ViewOf_names_the_view_an_element_is_in_overlays_and_popups_included()
	{
		var inner = Box();
		var panel = new StackPanel();
		panel.Children.Add(inner);
		var (host, view) = Hosted(panel);
		var second = host.CreateRoot(UILayer.Default);
		var elsewhere = Box();
		second.Root.Content = elsewhere;
		var popup = Box(10f, 10f);
		view.Root.AddOverlay(popup, Vector2.Zero);
		var overlay = new Border();
		Diagnostics.AddOverlay(second, overlay);

		Diagnostics.ViewOf(inner).ShouldBe(view);
		Diagnostics.ViewOf(popup).ShouldBe(view);
		Diagnostics.ViewOf(elsewhere).ShouldBe(second);
		Diagnostics.ViewOf(overlay).ShouldBe(second);
		Diagnostics.ViewOf(Box()).ShouldBeNull(); // in no tree
		Diagnostics.ViewOf(new UIRoot(new DrawListId(99)) { Content = Box() }.Content!).ShouldBeNull(); // a root no host made
	}

	[Test]
	public void ViewsVersion_moves_with_each_host_built_and_each_view_added_or_removed()
	{
		var start = Diagnostics.ViewsVersion;
		var host = new UIHost();
		var afterHost = Diagnostics.ViewsVersion;
		afterHost.ShouldNotBe(start);

		var view = host.CreateRoot(UILayer.Default);
		var afterAdd = Diagnostics.ViewsVersion;
		afterAdd.ShouldNotBe(afterHost);

		view.Root.Content = Box();
		host.Tick(0.016, 0f, Surface);
		Diagnostics.ViewsVersion.ShouldBe(afterAdd); // content and ticks are not views

		host.RemoveRoot(view);
		var afterRemove = Diagnostics.ViewsVersion;
		afterRemove.ShouldNotBe(afterAdd);
		host.RemoveRoot(view);
		Diagnostics.ViewsVersion.ShouldBe(afterRemove); // removing a view the host does not have changes nothing
	}

	// --- Properties ---

	[Test]
	public void Properties_reports_each_level_a_value_comes_from()
	{
		var text = new TextBlock { Text = "Local" };
		text.SetStyleValue(TextBlock.FontSizeProperty, 18f);
		text.SetAnimatedValue(UIElement.OpacityProperty, 0.5f);
		text.SetInheritedValue(UIElement.TabIndexProperty, 3);
		var templated = new ContentControl { Template = OpacityTemplate(), Opacity = 0.25f };
		var panel = new StackPanel();
		panel.Children.Add(text);
		panel.Children.Add(templated);
		Hosted(panel);
		var part = templated.GetTemplateRoot()!;

		var props = Diagnostics.Properties(text);
		Find(props, TextBlock.TextProperty).ShouldSatisfyAllConditions(
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Local),
			p => p.Value.ShouldBe("Local"));
		Find(props, TextBlock.FontSizeProperty).ShouldSatisfyAllConditions(
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Style),
			p => p.Value.ShouldBe(18f));
		Find(props, UIElement.OpacityProperty).ShouldSatisfyAllConditions(
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Animation),
			p => p.Value.ShouldBe(0.5f));
		Find(props, UIElement.TabIndexProperty).Level.ShouldBe(Diagnostics.ValueLevel.Inherited);
		Find(props, UIElement.IsHitTestVisibleProperty).ShouldSatisfyAllConditions(
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Default),
			p => p.Value.ShouldBe(true));
		Find(Diagnostics.Properties(part), UIElement.OpacityProperty).ShouldSatisfyAllConditions(
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Template),
			p => p.Value.ShouldBe(0.25f));
	}

	[Test]
	public void Properties_names_each_property_with_its_owner_and_value_type_base_types_first()
	{
		var text = new TextBlock();
		var props = Diagnostics.Properties(text);

		var opacity = Find(props, UIElement.OpacityProperty);
		opacity.Name.ShouldBe("Opacity");
		opacity.OwnerType.ShouldBe(typeof(UIElement));
		opacity.ValueType.ShouldBe(typeof(float));
		Find(props, TextBlock.FontSizeProperty).OwnerType.ShouldBe(typeof(TextBlock));

		// UIElement's, then FrameworkElement's, then TextBlock's own.
		props.Select(p => p.OwnerType).Distinct().ShouldBe([typeof(UIElement), typeof(FrameworkElement), typeof(TextBlock)]);
		props.Select(p => p.Property).ShouldNotContain(Control.IsEnabledProperty); // not a TextBlock property
	}

	[Test]
	public void Properties_lists_an_attached_property_only_where_it_is_set()
	{
		var text = new TextBlock();
		Diagnostics.Properties(text).Select(p => p.Property).ShouldNotContain(Grid.RowProperty);

		Grid.SetRow(text, 2);
		var row = Find(Diagnostics.Properties(text), Grid.RowProperty);
		row.OwnerType.ShouldBe(typeof(Grid));
		row.Level.ShouldBe(Diagnostics.ValueLevel.Local);
		row.Value.ShouldBe(2);
	}

	[Test]
	public void A_style_value_names_the_setter_it_came_from()
	{
		var setter = new Setter { Property = TextBlock.FontSizeProperty, Value = 24f };
		Diagnostics.RecordSource(setter, "Theme.xaml", 7, 6);
		var text = new TextBlock { Style = new Style { Setters = { setter } } };

		var fontSize = Find(Diagnostics.Properties(text), TextBlock.FontSizeProperty);
		fontSize.Level.ShouldBe(Diagnostics.ValueLevel.Style);
		fontSize.Source.ShouldBe(new Diagnostics.SourcePosition("Theme.xaml", 7, 6, InTemplate: false));
	}

	[Test]
	public void The_style_source_is_the_setter_that_won()
	{
		var baseSetter = new Setter { Property = TextBlock.FontSizeProperty, Value = 12f };
		var derivedSetter = new Setter { Property = TextBlock.FontSizeProperty, Value = 24f };
		Diagnostics.RecordSource(baseSetter, "Theme.xaml", 3, 6);
		Diagnostics.RecordSource(derivedSetter, "Theme.xaml", 9, 6);
		var text = new TextBlock
		{
			Style = new Style { BasedOn = new Style { Setters = { baseSetter } }, Setters = { derivedSetter } },
		};

		Find(Diagnostics.Properties(text), TextBlock.FontSizeProperty).Source!.Line.ShouldBe(9);
	}

	[Test]
	public void Reading_properties_twice_reports_the_same()
	{
		var text = new TextBlock { Text = "Same" };
		var first = Diagnostics.Properties(text);
		var second = Diagnostics.Properties(text);

		second.ShouldBe(first);
	}

	// --- SetLocal / ClearLocal ---

	[Test]
	public void SetLocal_wins_over_a_template_value_and_ClearLocal_reveals_it_again()
	{
		var templated = new ContentControl { Template = OpacityTemplate(), Opacity = 0.25f };
		Hosted(templated);
		var part = templated.GetTemplateRoot()!;

		Diagnostics.SetLocal(part, UIElement.OpacityProperty, 0.75f);
		part.Opacity.ShouldBe(0.75f);
		templated.Opacity = 0.5f;
		part.Opacity.ShouldBe(0.75f);

		Diagnostics.ClearLocal(part, UIElement.OpacityProperty);
		part.Opacity.ShouldBe(0.5f);
		Find(Diagnostics.Properties(part), UIElement.OpacityProperty).Level.ShouldBe(Diagnostics.ValueLevel.Template);
	}

	[Test]
	public void SetLocal_refuses_a_value_of_another_type()
	{
		var border = new Border();

		Should.Throw<ArgumentException>(() => Diagnostics.SetLocal(border, UIElement.OpacityProperty, 0.5d))
			.Message.ShouldContain("Opacity takes a Single");
		Should.Throw<ArgumentException>(() => Diagnostics.SetLocal(border, UIElement.OpacityProperty, null));
		border.HasLocalValue(UIElement.OpacityProperty).ShouldBeFalse();
	}

	[Test]
	public void SetLocal_refuses_a_property_the_element_does_not_have_but_takes_an_attached_one()
	{
		var border = new Border();

		Should.Throw<ArgumentException>(() => Diagnostics.SetLocal(border, TextBlock.FontSizeProperty, 12f))
			.Message.ShouldContain("TextBlock.FontSize is not a property of Border");
		Diagnostics.SetLocal(border, Grid.ColumnProperty, 1);
		Grid.GetColumn(border).ShouldBe(1);
	}

	[Test]
	public void SetLocal_takes_null_where_the_property_does()
	{
		var templated = new ContentControl { Template = OpacityTemplate() };
		Diagnostics.SetLocal(templated, Control.TemplateProperty, null);
		templated.Template.ShouldBeNull();
	}

	// --- HitTest and Bounds ---

	[Test]
	public void HitTest_finds_the_element_drawn_at_a_point_and_can_include_hidden_ones()
	{
		var box = Box();
		var (_, view) = Hosted(box);

		Diagnostics.HitTest(view, new Vector2(10, 10), includeHidden: false).ShouldBe(box);
		Diagnostics.HitTest(view, new Vector2(150, 150), includeHidden: false).ShouldBeNull();

		box.IsHitTestVisible = false;
		Diagnostics.HitTest(view, new Vector2(10, 10), includeHidden: false).ShouldBeNull();
		Diagnostics.HitTest(view, new Vector2(10, 10), includeHidden: true).ShouldBe(box);
	}

	[Test]
	public void HitTest_looks_through_a_diagnostics_overlay()
	{
		var box = Box();
		var (host, view) = Hosted(box);
		var overlay = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(0, 255, 0)) };
		Diagnostics.AddOverlay(view, overlay);
		host.Tick(0.016, 0f, Surface);

		view.Root.HitTest(new Vector2(10, 10)).ShouldBe(overlay); // input reaches the overlay first
		Diagnostics.HitTest(view, new Vector2(10, 10), includeHidden: false).ShouldBe(box); // a pick sees beneath it
	}

	[Test]
	public void Bounds_maps_an_element_to_surface_pixels_through_layout_render_transform_and_scale()
	{
		var box = Box(30f, 20f);
		box.Margin = new Thickness(10f, 5f, 0f, 0f);
		box.RenderTransform = new MatrixTransform { Matrix = Matrix3x2.CreateTranslation(4f, 0f) };
		Hosted(box, layoutScale: 2f);

		// Layout puts it at (10, 5) DIPs, the render transform adds 4, and the view's scale doubles it all.
		Diagnostics.Bounds(box).ShouldBe(new Rect(28f, 10f, 60f, 40f));
	}

	[Test]
	public void Bounds_is_null_for_an_element_in_no_view()
	{
		Diagnostics.Bounds(Box()).ShouldBeNull();
	}

	// --- The overlay and the pointer ---

	[Test]
	public void An_overlay_stays_above_popups_that_open_after_it()
	{
		var (host, view) = Hosted(Box());
		var overlay = new Border { Background = new SolidColorBrush(HdrColor.FromRgb(0, 255, 0)) };
		Diagnostics.AddOverlay(view, overlay);
		var popup = Box(20f, 20f);
		view.Root.AddOverlay(popup, Vector2.Zero);
		host.Tick(0.016, 0f, Surface);

		view.Root.Overlays[^1].Element.ShouldBe(overlay);
		view.Root.Overlays[^1].FullSurface.ShouldBeTrue();
		overlay.LayoutRect.Width.ShouldBe(Surface.X);
		Diagnostics.IsOverlay(overlay).ShouldBeTrue();
		Diagnostics.IsOverlay(popup).ShouldBeFalse();
	}

	[Test]
	public void RemoveOverlay_takes_an_overlay_down_and_only_an_overlay()
	{
		var (_, view) = Hosted(Box());
		var overlay = new Border();
		var popup = Box(20f, 20f);
		Diagnostics.AddOverlay(view, overlay);
		view.Root.AddOverlay(popup, Vector2.Zero);

		Diagnostics.RemoveOverlay(popup).ShouldBeFalse();
		Diagnostics.RemoveOverlay(overlay).ShouldBeTrue();
		Diagnostics.IsOverlay(overlay).ShouldBeFalse();
		view.Root.Overlays.Select(o => o.Element).ShouldBe([popup]);

		// With the overlay gone, a popup that opens next goes on top.
		var next = Box(5f, 5f);
		view.Root.AddOverlay(next, Vector2.Zero);
		view.Root.Overlays[^1].Element.ShouldBe(next);
	}

	[Test]
	public void AddOverlay_refuses_an_element_already_in_a_tree()
	{
		var child = Box();
		var (_, view) = Hosted(new Border { Child = child });

		Should.Throw<ArgumentException>(() => Diagnostics.AddOverlay(view, child));
	}

	[Test]
	public void HoldPointer_reports_the_pointer_wanted_at_once_and_until_released()
	{
		var (host, _) = Hosted(Box());
		host.WantsPointer.ShouldBeFalse();

		Diagnostics.HoldPointer(host, true);
		host.WantsPointer.ShouldBeTrue(); // before the next tick, so the click that follows is held back
		host.Tick(0.016, 0f, Surface);
		host.WantsPointer.ShouldBeTrue();

		Diagnostics.HoldPointer(host, false);
		host.Tick(0.016, 0f, Surface);
		host.WantsPointer.ShouldBeFalse();
	}

	// --- Values ---

	[Test]
	[Arguments(typeof(float), "1.5", 1.5f)]
	[Arguments(typeof(double), "-2", -2d)]
	[Arguments(typeof(int), "3", 3)]
	[Arguments(typeof(bool), "True", true)]
	[Arguments(typeof(string), " spaced ", " spaced ")]
	[Arguments(typeof(Visibility), "Collapsed", Visibility.Collapsed)]
	public void TryParseValue_reads_the_literal_forms(Type type, string text, object expected)
	{
		Diagnostics.TryParseValue(type, text, out var value).ShouldBeTrue();
		value.ShouldBe(expected);
	}

	[Test]
	public void TryParseValue_reads_the_structured_forms()
	{
		Parse<HdrColor>("#FF0000").ShouldBe(HdrColor.FromRgb(255, 0, 0));
		Parse<HdrColor>("#80FF0000").ShouldBe(HdrColor.FromRgb(255, 0, 0, 128));
		Parse<HdrColor>("CornflowerBlue").ShouldBe(HdrColor.FromRgb(100, 149, 237));
		Parse<Thickness>("4").ShouldBe(new Thickness(4f));
		Parse<Thickness>("1,2").ShouldBe(new Thickness(1f, 2f));
		Parse<Thickness>("1 2 3 4").ShouldBe(new Thickness(1f, 2f, 3f, 4f));
		Parse<Point>("3,4").ShouldBe(new Point(3f, 4f));
		Parse<Vector2>("1,2").ShouldBe(new Vector2(1f, 2f));
		Parse<Vector3>("1,2,3").ShouldBe(new Vector3(1f, 2f, 3f));
		Parse<GridLength>("Auto").ShouldBe(GridLength.Auto);
		Parse<GridLength>("*").ShouldBe(GridLength.Star);
		Parse<GridLength>("2.5*").ShouldBe(GridLength.Stars(2.5f));
		Parse<GridLength>("40").ShouldBe(GridLength.Pixels(40f));
		Parse<TimeSpan>("0:0:1.5").ShouldBe(TimeSpan.FromSeconds(1.5));
		Parse<RepeatBehavior>("3x").ShouldBe(RepeatBehavior.Times(3));
		Parse<RepeatBehavior>("Forever").ShouldBe(RepeatBehavior.Forever);
		Parse<HdrColor?>("#00FF00").ShouldBe(HdrColor.FromRgb(0, 255, 0));
	}

	[Test]
	public void TryParseValue_takes_x_null_only_where_null_is_allowed()
	{
		Diagnostics.TryParseValue(typeof(HdrColor?), "{x:Null}", out var color).ShouldBeTrue();
		color.ShouldBeNull();
		Diagnostics.TryParseValue(typeof(string), "{x:Null}", out var text).ShouldBeTrue();
		text.ShouldBeNull();
		Diagnostics.TryParseValue(typeof(float), "{x:Null}", out _).ShouldBeFalse();
	}

	[Test]
	[Arguments(typeof(float), "abc")]
	[Arguments(typeof(float), "NaN")]
	[Arguments(typeof(int), "1.5")]
	[Arguments(typeof(bool), "yes")]
	[Arguments(typeof(Visibility), "collapsed")]
	[Arguments(typeof(Visibility), "1")]
	[Arguments(typeof(Thickness), "1,2,3")]
	[Arguments(typeof(GridLength), "Fill")]
	[Arguments(typeof(HdrColor), "#12345")]
	[Arguments(typeof(Style), "Anything")]
	public void TryParseValue_refuses_what_the_generator_refuses(Type type, string text)
	{
		Diagnostics.TryParseValue(type, text, out var value).ShouldBeFalse();
		value.ShouldBeNull();
	}

	private static T Parse<T>(string text)
	{
		Diagnostics.TryParseValue(typeof(T), text, out var value).ShouldBeTrue();
		return (T)value!;
	}

	// --- Sources and the frame source ---

	[Test]
	public void SourceOf_returns_what_was_recorded_and_null_for_an_element_built_in_code()
	{
		var recorded = new Border();
		Diagnostics.RecordSource(recorded, "View.xaml", 12, 4, inTemplate: true);

		Diagnostics.SourceOf(recorded).ShouldBe(new Diagnostics.SourcePosition("View.xaml", 12, 4, InTemplate: true));
		Diagnostics.SourceOf(new Border()).ShouldBeNull();
	}

	private sealed class FakeFrames : Diagnostics.IFrameSource
	{
		public bool TryReadPixels(int x, int y, int width, int height, Span<byte> rgba8)
		{
			rgba8.Fill(0xFF);
			return true;
		}
	}

	[Test]
	public void SetFrameSource_hands_engine_glues_frames_to_a_tool()
	{
		var frames = new FakeFrames();
		try
		{
			Diagnostics.SetFrameSource(frames);
			var source = Diagnostics.FrameSource.ShouldNotBeNull();
			var pixels = new byte[4];
			source.TryReadPixels(0, 0, 1, 1, pixels).ShouldBeTrue();
			pixels.ShouldBe(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
		}
		finally
		{
			Diagnostics.SetFrameSource(null);
		}
		Diagnostics.FrameSource.ShouldBeNull();
	}
}
