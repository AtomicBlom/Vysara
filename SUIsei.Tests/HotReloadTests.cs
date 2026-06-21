using System.Numerics;

using SUIsei.Tests.Generated;

namespace SUIsei.Tests;

/// <summary>
/// XAML Hot Reload (PlanningUI.md MU6). Exercises the runtime rebuild path with no hot-reload agent by forcing
/// <see cref="HotReload.Enabled"/> on: a metadata update is simulated via <see cref="HotReload.NotifyUpdate"/>,
/// and the next <see cref="UIHost.Tick"/> drains it — re-running the generated <c>RebuildComponent()</c> and
/// re-hosting the fresh root. The registry is process-global, so these run isolated and reset around each test.
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
	public void Rebuild_swaps_in_a_fresh_root_and_rehosts()
	{
		try
		{
			var host = FreshHost();
			var view = new SampleView();
			view.InitializeComponent();
			var oldRoot = view.Root;

			var uiView = host.CreateRoot(UIPlacement.Overlay(0, 1f));
			uiView.Root.Content = view.Root;

			HotReload.NotifyUpdate([typeof(SampleView)]);
			host.Tick(0.016, 0f, new Vector2(800f, 600f));

			view.Root.ShouldNotBeSameAs(oldRoot);              // re-ran InitializeComponent → fresh tree
			uiView.Root.Content.ShouldBeSameAs(view.Root);     // and the host now points at the fresh root
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
			var uiView = host.CreateRoot(UIPlacement.Overlay(0, 1f));
			uiView.Root.Content = view.Root;
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
			var oldRoot = view.Root;
			host.CreateRoot(UIPlacement.Overlay(0, 1f)).Root.Content = view.Root;

			HotReload.NotifyUpdate([typeof(string)]);   // an unrelated type
			host.Tick(0.016, 0f, new Vector2(800f, 600f));

			view.Root.ShouldBeSameAs(oldRoot);          // untouched
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
		var oldRoot = view.Root;
		host.CreateRoot(UIPlacement.Overlay(0, 1f)).Root.Content = view.Root;

		HotReload.NotifyUpdate([typeof(SampleView)]);
		host.Tick(0.016, 0f, new Vector2(800f, 600f));

		view.Root.ShouldBeSameAs(oldRoot);              // nothing tracked, nothing rebuilt
	}
}
