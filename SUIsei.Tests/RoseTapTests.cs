using System.IO.Pipes;
using System.Numerics;

using RoseMcp.Tap.Protocol;

using SUIsei.Controls;
using SUIsei.RoseTap;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="Tap"/>, driven as RoseMCP's host drives it: the test makes the host's pipe, starts the tap with startup
/// data naming it, reads the greeting, then sends framed requests and ticks the game's host while it waits, since the
/// tap answers on the UI thread at the next tick. The served view is <c>Sample/TapView.xaml</c>, so rows are asserted
/// exactly, positions included.
/// <para>
/// The host registry and the posted-work queue are process-global, so these run apart from other tests, and each
/// clears the registry first so the tree holds only its own host. Also here: the diagnostics endpoint a game hosts,
/// which loads this same tap from its file and starts it.
/// </para>
/// </summary>
[NotInParallel]
public sealed class RoseTapTests
{
	private static readonly Vector2 Surface = new(400f, 400f);

	private static (UIHost Host, TapView View) Served()
	{
		HotReload.Reset();
		var host = new UIHost();
		var view = new TapView();
		view.InitializeComponent();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0.016, 0f, Surface);
		return (host, view);
	}

	private static string File(TapView view) => Diagnostics.SourceOf(view.Root)!.File;

	private static string WorkDir() => Path.Combine(Path.GetTempPath(), "suisei-rosetap-tests", Guid.NewGuid().ToString("N"));

	// RoseMCP's end of a session: the pipe it makes and names in the startup data, and the key the greeting presents.
	private sealed class FakeHost : IDisposable
	{
		private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

		private readonly NamedPipeServerStream _server;
		private readonly UIHost? _ticked;
		private uint _lastId;

		public FakeHost(UIHost? ticked)
		{
			_ticked = ticked;
			PipeName = $"suisei-rosetap-test-{Guid.NewGuid():N}";
			_server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		}

		public string PipeName { get; }

		public string Key { get; } = Guid.NewGuid().ToString("N");

		public string StartupData(string workDir) => $"{workDir}|{PipeName}|{Key}";

		/// <summary>Wait for the tap to connect, and return its greeting.</summary>
		public string Greeting()
		{
			_server.WaitForConnectionAsync().Wait(Bound).ShouldBeTrue("the tap did not connect");
			return Receive() ?? throw new EndOfStreamException("The tap hung up without greeting.");
		}

		/// <summary>Send one request and return the body of the reply to it, or null when the tap hung up instead.</summary>
		public string? Ask(string request)
		{
			var id = ++_lastId;
			try
			{
				TapWire.WriteMessage(_server, TapWire.Frame(id, request));
			}
			catch (IOException)
			{
				return null; // the tap has hung up, and the pipe broke under the write
			}
			if (Receive() is not { } frame)
				return null;
			TapWire.TryReadFrame(frame, out var answered, out var body).ShouldBeTrue();
			answered.ShouldBe(id);
			return body;
		}

		/// <summary>The rows of a reply, without the empty line its final newline leaves.</summary>
		public string[] Rows(string request) => (Ask(request) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

		// Reads the next message, ticking the game's host meanwhile: the tap answers on the UI thread at its next tick.
		private string? Receive()
		{
			var reading = TapWire.ReadMessageAsync(_server, CancellationToken.None);
			var deadline = DateTime.UtcNow + Bound;
			while (!reading.IsCompleted)
			{
				if (DateTime.UtcNow > deadline)
					throw new TimeoutException("The tap did not answer.");
				_ticked?.Tick(0.016, 0f, Surface);
				Thread.Sleep(1);
			}
			return reading.Result;
		}

		public void Dispose() => _server.Dispose();
	}

	private static FakeHost Connected(UIHost host)
	{
		var fake = new FakeHost(host);
		Tap.Start(fake.StartupData(WorkDir()));
		fake.Greeting();
		return fake;
	}

	// --- Starting and greeting ---

	[Test]
	public void Start_greets_with_the_key_and_exactly_what_it_serves()
	{
		var (host, _) = Served();
		using var fake = new FakeHost(host);
		try
		{
			Tap.Start(fake.StartupData(WorkDir()));
			var greeting = fake.Greeting();

			greeting.ShouldBe($"RoseTap/{TapGreeting.ProtocolVersion}/{fake.Key}\ndetach properties selecthandle tree");
			TapGreeting.TryRead(greeting, out var version, out var key, out var capabilities).ShouldBeTrue();
			version.ShouldBe(5);
			key.ShouldBe(fake.Key);
			capabilities.Names.Order(StringComparer.Ordinal).ShouldBe(["detach", "properties", "selecthandle", "tree"]);
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Start_refuses_startup_data_that_is_not_three_fields()
	{
		Should.Throw<ArgumentException>(() => Tap.Start("only|two")).Message.ShouldContain("workDir|pipeName|key");
	}

	[Test]
	public void The_tap_logs_to_the_work_folder_the_host_named()
	{
		var (host, _) = Served();
		var workDir = WorkDir();
		using var fake = new FakeHost(host);
		try
		{
			Tap.Start(fake.StartupData(workDir));
			fake.Greeting();
		}
		finally
		{
			Tap.Stop();
		}

		System.IO.File.ReadAllText(Path.Combine(workDir, "suisei-rosetap.log")).ShouldContain($"Connected to {fake.PipeName} and greeted");
	}

	// --- tree ---

	[Test]
	public void Tree_answers_a_row_per_element_with_its_address()
	{
		var (host, view) = Served();
		using var fake = Connected(host);
		try
		{
			var f = File(view);
			fake.Rows("tree").ShouldBe(
			[
				TapRows.Tree(1, 0, 0, "SUIsei.Controls.StackPanel", "", f, 8, 2, "StackPanel[0]"),
				TapRows.Tree(2, 1, 0, "SUIsei.Controls.TextBlock", "Title", f, 16, 3, "#Title"),
				TapRows.Tree(3, 1, 1, "SUIsei.Controls.Border", "", f, 17, 3, "StackPanel[0]/Border[0]"),
				TapRows.Tree(4, 3, 0, "SUIsei.Controls.TextBlock", "", f, 18, 4, "StackPanel[0]/Border[0]/TextBlock[0]"),
				TapRows.Tree(5, 1, 2, "SUIsei.Controls.Border", "", f, 20, 3, "StackPanel[0]/Border[1]"),
				TapRows.Tree(6, 5, 0, "SUIsei.Controls.StackPanel", "", f, 21, 4, "StackPanel[0]/Border[1]/StackPanel[0]"),
				TapRows.Tree(7, 6, 0, "SUIsei.Controls.TextBlock", "", f, 22, 5, "StackPanel[0]/Border[1]/StackPanel[0]/TextBlock[0]"),
				TapRows.Tree(8, 6, 1, "SUIsei.Controls.TextBlock", "", f, 23, 5, "StackPanel[0]/Border[1]/StackPanel[0]/TextBlock[1]"),
				TapRows.Tree(9, 1, 3, "SUIsei.Controls.ContentControl", "Card", f, 26, 3, "#Card"),
				TapRows.Tree(10, 9, 0, "SUIsei.Controls.Border", "Chrome", f, 29, 6, "#Chrome"),
				TapRows.Tree(11, 1, 4, "SUIsei.Controls.Button", "Go", f, 33, 3, "#Go"),
				TapRows.Tree(12, 11, 0, "SUIsei.Controls.Border", "", "", 0, 0, "#Go/Border[0]"),
				TapRows.Tree(13, 12, 0, "SUIsei.Controls.ContentPresenter", "", "", 0, 0, "#Go/Border[0]/ContentPresenter[0]"),
				TapRows.Tree(14, 13, 0, "SUIsei.Controls.TextBlock", "", "", 0, 0, "#Go/Border[0]/ContentPresenter[0]/TextBlock[0]"),
			]);
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Handles_stay_the_same_across_reads_and_count_the_roots_of_every_view_together()
	{
		var (host, view) = Served();
		var second = host.CreateRoot(UILayer.Default);
		second.Root.Content = new StackPanel();
		host.Tick(0.016, 0f, Surface);
		using var fake = Connected(host);
		try
		{
			var first = fake.Rows("tree");
			var again = fake.Rows("tree");

			again.ShouldBe(first);
			var secondRoot = TapWire.Fields(first.Last());
			(secondRoot[1], secondRoot[2], secondRoot[8]).ShouldBe(("0", "1", "StackPanel[1]"));
		}
		finally
		{
			Tap.Stop();
		}
	}

	// --- properties ---

	[Test]
	public void Properties_answers_ok_the_element_and_its_set_values_with_where_each_came_from()
	{
		var (host, view) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Rows("tree");
			var f = File(view);

			fake.Rows("properties 2").ShouldBe(
			[
				"ok",
				TapRows.Element("SUIsei.Controls.TextBlock", f, 16, 3),
				TapRows.Property("Style", "SUIsei.Style", "SUIsei.Style", "SUIsei.FrameworkElement", "Local", "", 0, 0, false, null),
				TapRows.Property("Text", "Hello", "System.String", "SUIsei.Controls.TextBlock", "Local", "", 0, 0, false, null),
				TapRows.Property("FontSize", "18", "System.Single", "SUIsei.Controls.TextBlock", "Style", f, 13, 5, false, null),
			]);
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Properties_all_adds_the_defaults_and_marks_a_null_value()
	{
		var (host, _) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Rows("tree");
			var rows = fake.Rows("properties 4 all");

			rows[0].ShouldBe("ok");
			rows.ShouldContain(TapRows.Property("Opacity", "1", "System.Single", "SUIsei.UIElement", "Default", "", 0, 0, false, null));
			rows.ShouldContain(TapRows.Property("Visibility", "Visible", "SUIsei.Visibility", "SUIsei.UIElement", "Default", "", 0, 0, false, null));
			rows.ShouldContain(TapRows.Property("IsHitTestVisible", "True", "System.Boolean", "SUIsei.UIElement", "Default", "", 0, 0, false, null));
			rows.ShouldContain(TapRows.Property("Style", null, "SUIsei.Style", "SUIsei.FrameworkElement", "Default", "", 0, 0, false, null));
			rows.ShouldContain(TapRows.Property("Text", "first", "System.String", "SUIsei.Controls.TextBlock", "Local", "", 0, 0, false, null));
			TapWire.Fields(rows.Single(r => r.StartsWith("P\tStyle\t", StringComparison.Ordinal)))[9].ShouldBe("1");
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void A_template_bound_value_reads_as_parent_template_and_an_attached_one_by_its_owner()
	{
		var (host, _) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Rows("tree");

			fake.Rows("properties 10").ShouldContain(
				TapRows.Property("Opacity", "0.5", "System.Single", "SUIsei.UIElement", "ParentTemplate", "", 0, 0, false, null));
			fake.Rows("properties 7").ShouldContain(
				TapRows.Property("Grid.Row", "2", "System.Int32", "SUIsei.Controls.Grid", "Local", "", 0, 0, false, null));
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Properties_of_a_handle_that_names_nothing_is_error_alone()
	{
		var (host, _) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Rows("tree");

			fake.Ask("properties 999").ShouldBe("error");
			fake.Ask("properties 0").ShouldBe("error");
			fake.Ask("properties x").ShouldBe("error");
		}
		finally
		{
			Tap.Stop();
		}
	}

	// --- selecthandle, detach and the rest ---

	[Test]
	public void Selecthandle_answers_selected_for_a_live_element_and_none_otherwise()
	{
		var (host, _) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Rows("tree");

			fake.Ask("selecthandle 2").ShouldBe("selected");
			fake.Ask("selecthandle 999").ShouldBe("none");
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void A_request_the_tap_does_not_serve_is_answered_empty()
	{
		var (host, _) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Ask("select rulers").ShouldBe("");
			fake.Ask("ask 0").ShouldBe("");
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Detach_answers_released_then_hangs_up_and_forgets_its_handles()
	{
		var (host, _) = Served();
		using var fake = Connected(host);
		try
		{
			fake.Rows("tree");

			fake.Ask("detach").ShouldBe("released");
			fake.Ask("tree").ShouldBeNull(); // the pipe is closed
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Starting_again_is_a_reconnect_that_ends_the_old_session()
	{
		var (host, _) = Served();
		using var first = Connected(host);
		using var second = new FakeHost(host);
		try
		{
			Tap.Start(second.StartupData(WorkDir()));
			second.Greeting().ShouldStartWith($"RoseTap/5/{second.Key}");

			first.Ask("tree").ShouldBeNull(); // the first session's pipe was closed
			second.Rows("tree").Length.ShouldBe(14); // handles start again in the new session
			TapWire.Fields(second.Rows("tree")[0])[0].ShouldBe("1");
		}
		finally
		{
			Tap.Stop();
		}
	}

	[Test]
	public void Stop_ends_the_session()
	{
		var (host, _) = Served();
		using var fake = Connected(host);

		Tap.Stop();

		fake.Ask("tree").ShouldBeNull();
	}

	// --- The endpoint ---

	[Test]
	public void A_host_brings_up_the_endpoint_which_greets_as_the_contract_says()
	{
		_ = new UIHost();
		using var client = new NamedPipeClientStream(".", EndpointWire.PipeName("suisei-diag-{pid}", Environment.ProcessId), PipeDirection.InOut);
		client.Connect(TimeSpan.FromSeconds(10));

		var greeting = TapWire.ReadMessage(client).ShouldNotBeNull();
		EndpointWire.TryReadGreeting(greeting, out var version, out var framework, out var frameworkVersion).ShouldBeTrue();
		version.ShouldBe(EndpointWire.Version);
		framework.ShouldBe("SUIsei");
		frameworkVersion.ShouldNotBeNullOrEmpty();
	}

	[Test]
	public void The_endpoint_loads_the_tap_from_its_file_and_the_tap_serves_the_game()
	{
		var (host, _) = Served();
		using var fake = new FakeHost(host);
		using var client = new NamedPipeClientStream(".", EndpointWire.PipeName("suisei-diag-{pid}", Environment.ProcessId), PipeDirection.InOut);
		client.Connect(TimeSpan.FromSeconds(10));
		TapWire.ReadMessage(client).ShouldNotBeNull().ShouldStartWith(EndpointWire.GreetingPrefix);

		TapWire.WriteMessage(client, EndpointWire.LoadRequest(typeof(Tap).Assembly.Location, "SUIsei.RoseTap.Tap", fake.StartupData(WorkDir())));
		var (loaded, detail) = EndpointWire.ReadReply(TapWire.ReadMessage(client).ShouldNotBeNull());
		loaded.ShouldBeTrue(detail);

		fake.Greeting().ShouldStartWith($"RoseTap/5/{fake.Key}");
		fake.Rows("tree").Length.ShouldBe(14);
		fake.Ask("detach").ShouldBe("released");
	}
}
