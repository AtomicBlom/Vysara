using System.Diagnostics;
using System.Numerics;

using BinaryVibrance.Vysara;
using BinaryVibrance.Vysara.RoseTap.Sample;

// A headless game for RoseMCP to attach to: one view on one UIHost, ticked at about 60 Hz until the process is killed.
// The runtime config turns BinaryVibrance.Vysara.Diagnostics on, and starting the endpoint brings up the pipe a RoseMCP host loads
// BinaryVibrance.Vysara.RoseTap through.

var view = new MainView();
view.InitializeComponent();

var host = new UIHost();
host.CreateRoot(UILayer.Default).Root.Content = view.Root;
DiagnosticsEndpoint.Start();

var pid = Environment.ProcessId;
Console.WriteLine($"BinaryVibrance.Vysara.RoseTap.Sample pid {pid}");
Console.WriteLine(BinaryVibrance.Vysara.Diagnostics.Enabled
	? $"Diagnostics on; the endpoint listens on vysara-diag-{pid}. Ticking at 60 Hz until killed."
	: "Diagnostics are off, so there is no endpoint: the runtime config did not turn BinaryVibrance.Vysara.Diagnostics.Enabled on, or this is a NativeAOT build.");

var surface = new Vector2(800f, 600f);
var clock = Stopwatch.StartNew();
var last = TimeSpan.Zero;
while (true)
{
	var now = clock.Elapsed;
	host.Tick((now - last).TotalSeconds, (float)now.TotalSeconds, surface);
	host.Render();
	last = now;
	Thread.Sleep(16);
}
