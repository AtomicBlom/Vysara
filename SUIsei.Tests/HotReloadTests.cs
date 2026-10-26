using System.Numerics;

using BindingView = SUIsei.Tests.Sample.BindingView;
using SampleView = SUIsei.Tests.Sample.SampleView;

namespace SUIsei.Tests;

/// <summary>
/// XAML Hot Reload. Exercises the runtime rebuild path with no hot-reload agent by forcing
/// <see cref="HotReload.Enabled"/> on: a metadata update is simulated via <see cref="HotReload.NotifyUpdate"/>,
/// and the next <see cref="UIHost.Tick"/> drains it — re-running the generated <c>RebuildComponent()</c> and
/// re-hosting the view. The registry is process-global, so these run isolated and reset around each test.
/// <para>
/// With the composition model (sealed root types like StackPanel) the view wraps a Root UIElement that is
/// reassigned on rebuild; the host is updated to the new Root. Tests verify that named fields are recreated.
/// </para>
/// </summary>
[NotInParallel]
public sealed class HotReloadTests
{
	private static UIHost FreshHost()
	{
		HotReload.Reset();
		HotReload.Enabled = true;
		return new UIHost();
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
}
