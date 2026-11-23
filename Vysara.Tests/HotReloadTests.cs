using System.Numerics;
using Vysara.Controls;

using BindingView = Vysara.Tests.Sample.BindingView;
using CounterPanel = Vysara.Tests.Sample.CounterPanel;
using NestingView = Vysara.Tests.Sample.NestingView;
using SampleView = Vysara.Tests.Sample.SampleView;

namespace Vysara.Tests;

/// <summary>
/// XAML Hot Reload. Exercises the runtime rebuild path with no hot-reload agent by forcing
/// <see cref="HotReload.Enabled"/> on: a metadata update is simulated via <see cref="HotReload.NotifyUpdate"/>,
/// and the next <see cref="UIHost.Tick"/> drains it — re-running the generated <c>RebuildComponent()</c> of each view
/// in a live tree, at any depth. The registry is process-global, so these run isolated and reset around each test.
/// <para>
/// With the composition model (sealed root types like StackPanel) the view wraps a Root UIElement that is
/// reassigned on rebuild, and the new Root takes the old one's place in its parent. Tests verify that named fields are
/// recreated, and that the code-behind's <c>OnComponentRebuilt</c> runs once the rebuilt tree is in place.
/// </para>
/// </summary>
[NotInParallel(TestIsolation.HotReload)]
[SkipWithoutDynamicCode]
public sealed class HotReloadTests
{
	private static readonly Vector2 Viewport = new(400f, 300f);

	private static UIHost FreshHost()
	{
		HotReload.Reset();
		HotReload.Enabled = true;
		return new UIHost();
	}

	// Host content in a new root of the host and lay it out once, so templates are applied.
	private static void Show(UIHost host, UIElement content)
	{
		host.CreateRoot(UILayer.Default).Root.Content = content;
		host.Tick(0.016, 0f, Viewport);
	}

	// Simulate a metadata update for the types, and drain it.
	private static void Reload(UIHost host, Type[]? types)
	{
		HotReload.NotifyUpdate(types);
		host.Tick(0.016, 0f, Viewport);
	}

	[Test]
	public void Rebuild_reconstructs_named_children_in_place()
	{
		try
		{
			var host = FreshHost();
			var view = new SampleView();
			view.InitializeComponent();
			var oldTitle = view.Title; // a named child — gets a new instance on rebuild

			var uiView = host.CreateRoot(UILayer.Default);
			uiView.Root.Content = view.Root; // view.Root is the hosted StackPanel

			HotReload.NotifyUpdate([typeof(SampleView)]);
			host.Tick(0.016, 0f, new Vector2(800f, 600f));

			view.Title.ShouldNotBeSameAs(oldTitle);         // named field re-created in-place
			uiView.Root.Content.ShouldBeSameAs(view.Root);  // new Root hosted after rebuild
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Rebuild_points_storyboards_at_the_rebuilt_elements()
	{
		try
		{
			var host = FreshHost();
			var view = new Sample.AnimationView();
			view.InitializeComponent();
			var uiView = host.CreateRoot(UILayer.Default);
			uiView.Root.Content = view.Root;
			var oldBox = view.Box;

			HotReload.NotifyUpdate([typeof(Sample.AnimationView)]);
			host.Tick(0.016, 0f, new Vector2(300f, 200f));
			view.Box.ShouldNotBeSameAs(oldBox);

			// The rebuilt visual states belong to the rebuilt host and animate the rebuilt box, not the discarded one.
			Animation.VisualStateManager.GoToState(view.Host, "Moved", useTransitions: true).ShouldBeTrue();
			host.Tick(0.5, 0f, new Vector2(300f, 200f));
			Controls.Canvas.GetLeft(view.Box).ShouldBe(50f, 1e-4f);
			Controls.Canvas.GetLeft(oldBox).ShouldBe(0f);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Rebuild_releases_the_old_tree_from_the_brushes_it_showed()
	{
		try
		{
			var host = FreshHost();
			var view = new Sample.SharedBrushView();
			view.InitializeComponent();
			var uiView = host.CreateRoot(UILayer.Default);
			uiView.Root.Content = view.Root;
			host.Tick(0.016, 0f, new Vector2(300f, 200f));
			var (oldAccent, oldFirst) = ResourceBrushAndFirstUser(view);
			oldAccent.ListenerCount.ShouldBe(4); // First, Second, Label and Styled

			HotReload.NotifyUpdate([typeof(Sample.SharedBrushView)]);
			host.Tick(0.016, 0f, new Vector2(300f, 200f));

			// The old tree left the host, so nothing of it is registered with the brush it showed, and the rebuilt tree
			// registered with its own resource brush.
			oldAccent.ListenerCount.ShouldBe(0);
			view.First.Background.ShouldNotBeSameAs(oldAccent);
			((Media.SolidColorBrush)view.First.Background!).ListenerCount.ShouldBe(4);

			// The old brush, still held here, keeps none of the old tree alive.
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			oldFirst.IsAlive.ShouldBeFalse();
			GC.KeepAlive(oldAccent);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	// The view's resource brush, and a weak reference to its first user, read where no local of this frame outlives them.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static (Media.SolidColorBrush Accent, WeakReference First) ResourceBrushAndFirstUser(Sample.SharedBrushView view) =>
		((Media.SolidColorBrush)view.First.Background!, new WeakReference(view.First));

	[Test]
	public void Rebuild_reattaches_bindings_to_the_view_model()
	{
		try
		{
			var host = FreshHost();
			var view = new BindingView();   // ctor runs InitializeComponent → tracked + bound
			var uiView = host.CreateRoot(UILayer.Default);
			uiView.Root.Content = view.Root; // view.Root is the hosted StackPanel
			view.Label.Text.ShouldBe("start");

			HotReload.NotifyUpdate([typeof(BindingView)]);
			host.Tick(0.016, 0f, new Vector2(800f, 600f));

			// The fresh binding subscription tracks the same (preserved) view model; the old one was detached
			// by StopTracking, so this drives only the rebuilt label.
			uiView.Root.Content.ShouldBeSameAs(view.Root);
			view.Label.Text.ShouldBe("start");
			view.ViewModel.Caption = "changed";
			view.Label.Text.ShouldBe("changed");
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Only_matching_types_are_rebuilt()
	{
		try
		{
			var host = FreshHost();
			var view = new SampleView();
			view.InitializeComponent();
			var oldTitle = view.Title;
			host.CreateRoot(UILayer.Default).Root.Content = view.Root;

			HotReload.NotifyUpdate([typeof(string)]);   // an unrelated type
			host.Tick(0.016, 0f, new Vector2(800f, 600f));

			view.Title.ShouldBeSameAs(oldTitle);        // untouched — same child instance
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Tracking_is_inert_when_disabled()
	{
		HotReload.Reset();
		HotReload.Enabled = false;

		var host = new UIHost();
		var view = new SampleView();
		view.InitializeComponent();
		var oldTitle = view.Title;
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;

		HotReload.NotifyUpdate([typeof(SampleView)]);
		host.Tick(0.016, 0f, new Vector2(800f, 600f));

		view.Title.ShouldBeSameAs(oldTitle);            // nothing tracked, nothing rebuilt
	}

	[Test]
	public void A_rebuild_calls_the_hook_once_with_its_bindings_and_new_root_in_place()
	{
		try
		{
			var host = FreshHost();
			var view = new NestingView();
			var frame = new Border { Child = view.Root };
			Show(host, frame);
			view.Rebuilds.ShouldBe(0); // building the view the first time is not a rebuild
			var (oldBefore, oldRoot) = (view.Before, view.Root);

			view.ViewModel.Caption = "changed";
			Reload(host, [typeof(NestingView)]);

			view.Rebuilds.ShouldBe(1);
			// The hook saw the rebuilt elements, their bindings showing the view model, and the new root where the old
			// one had stood.
			var (before, text) = view.BeforeAtRebuild.ShouldNotBeNull();
			before.ShouldNotBeSameAs(oldBefore);
			before.ShouldBeSameAs(view.Before);
			text.ShouldBe("changed");
			var (root, parent) = view.RootAtRebuild.ShouldNotBeNull();
			root.ShouldNotBeSameAs(oldRoot);
			parent.ShouldBeSameAs(frame);
			frame.Child.ShouldBeSameAs(view.Root);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void A_view_nested_in_another_s_xaml_rebuilds_in_place_and_keeps_its_place()
	{
		try
		{
			var host = FreshHost();
			var view = new NestingView();
			Show(host, view.Root);
			var inner = view.Inner;
			var (oldLabel, before) = (inner.Label, view.Before);

			Reload(host, [typeof(CounterPanel)]);

			inner.Rebuilds.ShouldBe(1);
			inner.Label.ShouldNotBeSameAs(oldLabel);       // rebuilt, on the same object
			view.Inner.ShouldBeSameAs(inner);
			view.Root.Children.Count.ShouldBe(3);
			view.Root.Children[1].ShouldBeSameAs(inner);   // which its parent keeps where it was
			inner.Label.AttachedRoot.ShouldNotBeNull();    // in the live tree
			view.Rebuilds.ShouldBe(0);                     // and the view around it is untouched
			view.Before.ShouldBeSameAs(before);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	[Arguments("Panel")]
	[Arguments("Border")]
	[Arguments("ScrollViewer")]
	[Arguments("ContentControl")]
	[Arguments("TemplatedContentControl")]
	[Arguments("ContentPresenter")]
	public void A_nested_composition_view_s_new_root_takes_the_old_one_s_place(string parentKind)
	{
		try
		{
			var host = FreshHost();
			var view = new NestingView();
			var oldRoot = view.Root;
			var (parent, shown) = Around(parentKind, oldRoot);
			Show(host, new Border { Child = parent }); // a level below the hosted content
			shown().ShouldBeSameAs(oldRoot);

			Reload(host, [typeof(NestingView)]);

			view.Rebuilds.ShouldBe(1);
			view.Root.ShouldNotBeSameAs(oldRoot);
			shown().ShouldBeSameAs(view.Root);
			view.Root.AttachedRoot.ShouldNotBeNull();
			oldRoot.Parent.ShouldBeNull();
			oldRoot.AttachedRoot.ShouldBeNull();
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	// A parent of the given kind holding the element, and what reads back the element it shows in that place.
	private static (UIElement Parent, Func<UIElement?> Shown) Around(string kind, UIElement element)
	{
		switch (kind)
		{
			case "Panel":
				var panel = new StackPanel();
				panel.Children.Add(new TextBlock { Text = "first" });
				panel.Children.Add(element);
				panel.Children.Add(new TextBlock { Text = "last" });
				return (panel, () => panel.Children.Count == 3 ? panel.Children[1] : null);
			case "Border":
				var border = new Border { Child = element };
				return (border, () => border.Child);
			case "ScrollViewer":
				var scrollViewer = new ScrollViewer { Content = element };
				return (scrollViewer, () => scrollViewer.Content);
			case "ContentControl":
				var control = new ContentControl { Content = element };
				return (control, () => control.Content as UIElement);
			case "TemplatedContentControl":
				// A button shows its content through the ContentPresenter in its template.
				var button = new Button { Content = element };
				return (button, () => Presented(button));
			case "ContentPresenter":
				var contentPresenter = new ContentPresenter { Content = element };
				return (contentPresenter, () => contentPresenter.Content as UIElement);
			default:
				throw new ArgumentOutOfRangeException(nameof(kind));
		}
	}

	// What the button's template presents, when it is the button's content.
	private static UIElement? Presented(Button button)
	{
		var presenter = (ContentPresenter)((Border)button.GetTemplateRoot()!).Child!;
		var presented = presenter.VisualChildren[0];
		return ReferenceEquals(button.Content, presented) ? presented : null;
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public void When_an_outer_and_an_inner_view_both_change_each_live_view_rebuilds_once(bool everything)
	{
		try
		{
			var host = FreshHost();
			var view = new NestingView();
			Show(host, view.Root);
			var oldInner = view.Inner;

			// The inner view registered first (the outer's InitializeComponent made it), but the outer rebuilds first.
			Reload(host, everything ? null : [typeof(CounterPanel), typeof(NestingView)]);

			view.Rebuilds.ShouldBe(1);
			oldInner.Rebuilds.ShouldBe(0);            // it left the live tree with the old root before its turn
			view.Inner.ShouldNotBeSameAs(oldInner);
			view.Inner.Rebuilds.ShouldBe(0);          // the new one was built from the new XAML, not rebuilt
			view.Root.Children[1].ShouldBeSameAs(view.Inner);
			view.Inner.AttachedRoot.ShouldNotBeNull();
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void A_nested_view_outside_a_live_host_s_tree_is_skipped()
	{
		try
		{
			var host = FreshHost();
			var unhosted = new NestingView();
			var standalone = new NestingView();
			_ = new UIRoot(UISurface.Hud) { Content = standalone.Root }; // a root, but no host's
			var detached = new NestingView();
			Show(host, detached.Root);
			var detachedInner = detached.Inner;
			detached.Root.Children.RemoveAt(1);
			var hosted = new NestingView();
			Show(host, hosted.Root);

			Reload(host, [typeof(CounterPanel)]);

			unhosted.Inner.Rebuilds.ShouldBe(0);
			standalone.Inner.Rebuilds.ShouldBe(0);
			detachedInner.Rebuilds.ShouldBe(0);
			hosted.Inner.Rebuilds.ShouldBe(1);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	[Arguments("Popup")]
	[Arguments("ItemsControl")]
	[Arguments("ListView")]
	public void A_composition_view_whose_parent_cannot_take_a_new_root_is_skipped(string parentKind)
	{
		try
		{
			var host = FreshHost();
			var view = new NestingView();
			var oldRoot = view.Root;
			var content = new StackPanel();
			Popup? popup = null;
			switch (parentKind)
			{
				case "Popup":
					popup = new Popup { Child = oldRoot };
					content.Children.Add(popup);
					break;
				case "ItemsControl":
					content.Children.Add(new ItemsControl { ItemsSource = new[] { oldRoot } });
					break;
				case "ListView":
					content.Children.Add(new ListView { ItemsSource = new[] { oldRoot } });
					break;
			}
			Show(host, content);
			if (popup is not null)
				popup.IsOpen = true; // the root is an overlay, in the live tree with no parent to find
			oldRoot.AttachedRoot.ShouldNotBeNull();

			Reload(host, [typeof(NestingView)]);

			view.Rebuilds.ShouldBe(0);
			view.Root.ShouldBeSameAs(oldRoot);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Nested_views_are_inert_when_disabled()
	{
		try
		{
			var host = FreshHost();
			var view = new NestingView();
			Show(host, view.Root);
			var (inner, oldRoot) = (view.Inner, view.Root);

			HotReload.NotifyUpdate([typeof(NestingView), typeof(CounterPanel)]);
			HotReload.Enabled = false; // switched off with the rebuilds still pending
			host.Tick(0.016, 0f, Viewport);

			view.Rebuilds.ShouldBe(0);
			inner.Rebuilds.ShouldBe(0);
			view.Root.ShouldBeSameAs(oldRoot);
		}
		finally
		{
			HotReload.Enabled = false;
			HotReload.Reset();
		}
	}

	[Test]
	public void Calling_RebuildComponent_puts_the_new_root_in_place_with_hot_reload_off()
	{
		HotReload.Reset();
		HotReload.Enabled = false;

		var view = new NestingView();
		var frame = new Border { Child = view.Root };
		view.RebuildComponent();

		frame.Child.ShouldBeSameAs(view.Root);
		view.Rebuilds.ShouldBe(1);
	}

	[Test]
	[Arguments("StackPanel", true)]
	[Arguments("ContentControl", false)]
	public void The_generated_rebuild_ends_by_calling_the_hook_for_either_kind_of_root(string root, bool composition)
	{
		var xaml = $"""<{root} xmlns="https://github.com/AtomicBlom/Vysara/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="App.View" />""";
		const string Hooked = "namespace App; partial class View { public int Rebuilds; partial void OnComponentRebuilt() => Rebuilds++; }";

		var text = XamlGeneratorHarness.Run(xaml, Hooked).AllGeneratedText();
		var rebuild = text[text.IndexOf("public void RebuildComponent()", StringComparison.Ordinal)..];
		rebuild = rebuild[..rebuild.IndexOf('}')];
		rebuild.TrimEnd().ShouldEndWith("OnComponentRebuilt();");
		rebuild.Contains("global::Vysara.HotReload.ReplaceRoot(__oldRoot, Root);", StringComparison.Ordinal).ShouldBe(composition);
		text.ShouldContain("partial void OnComponentRebuilt();");

		// It compiles implemented or not.
		XamlGeneratorHarness.CompileErrors(xaml, Hooked).ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(xaml, "namespace App; partial class View { }").ShouldBeEmpty();
	}
}
