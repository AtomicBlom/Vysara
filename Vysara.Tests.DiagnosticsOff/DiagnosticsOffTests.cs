using System.IO.Pipes;
using System.Numerics;
using Vysara.Controls;

namespace Vysara.Tests.DiagnosticsOff;

/// <summary>
/// Vysara with <c>Vysara.Diagnostics.Enabled</c> never set, as a game ships: the members that need diagnostics throw,
/// the two that generated code and engine glue call without asking do nothing, a view builds and ticks, and the
/// endpoint never listens.
/// </summary>
public sealed class DiagnosticsOffTests
{
	private static readonly Vector2 Surface = new(200f, 100f);

	[Test]
	public void Diagnostics_are_off()
	{
		Diagnostics.Enabled.ShouldBeFalse();
	}

	[Test]
	public void The_members_that_need_diagnostics_throw()
	{
		var host = new UIHost();

		Should.Throw<InvalidOperationException>(() => Diagnostics.Hosts);
		Should.Throw<InvalidOperationException>(() => Diagnostics.ViewsVersion);
		Should.Throw<InvalidOperationException>(() => Diagnostics.Post(() => { }));
		Should.Throw<InvalidOperationException>(() => Diagnostics.Post(host, () => { }));
		Should.Throw<InvalidOperationException>(() => Diagnostics.ChildrenOf(new Border()));
	}

	/// <summary>Generated code records every element's position without asking first, so with diagnostics off it must cost
	/// nothing and throw nothing.</summary>
	[Test]
	public void A_generated_view_builds_and_ticks()
	{
		var view = new OffView();
		view.InitializeComponent();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;

		host.Tick(0.016, 0f, Surface);
		host.Render();

		view.Title.Text.ShouldBe("Off");
	}

	[Test]
	public void Recording_a_source_and_setting_a_frame_source_do_nothing()
	{
		Diagnostics.RecordSource(new Border(), "View.xaml", 1, 1, inTemplate: false);
		Diagnostics.SetFrameSource(null);
	}

	[Test]
	public void Starting_the_endpoint_does_nothing_and_no_pipe_listens()
	{
		DiagnosticsEndpoint.Start().ShouldBeFalse();
		_ = new UIHost();

		using var client = new NamedPipeClientStream(".", $"vysara-diag-{Environment.ProcessId}", PipeDirection.InOut);
		Should.Throw<TimeoutException>(() => client.Connect(TimeSpan.FromMilliseconds(250)));
	}
}
