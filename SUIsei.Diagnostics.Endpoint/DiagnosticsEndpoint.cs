using System.Diagnostics;
using System.Reflection;
using RoseMcp.Tap.Endpoint;
using RoseMcp.Tap.Protocol;

namespace SUIsei;

/// <summary>
/// The diagnostics endpoint, on the pipe <c>suisei-diag-{pid}</c>, through which a RoseMCP host loads a tap such as
/// SUIsei.RoseTap into a running game, whether it launched the game or attached to it later. A tap reaches the game
/// through <see cref="Diagnostics"/> alone.
/// <para>
/// A game opts in twice: it turns <see cref="Diagnostics"/> on in its runtime config, and it calls <see cref="Start"/>.
/// The endpoint loads an assembly a client on the pipe names and runs it, so a game that only wants the in-process
/// <see cref="Diagnostics"/> API, for an overlay of its own, never calls <see cref="Start"/>. The pipe accepts only the
/// game's own user; with <c>tapDirectories</c>, only taps in those folders load.
/// </para>
/// </summary>
public static class DiagnosticsEndpoint
{
	// The pipe the endpoint listens on, as SUIsei.RoseTap's RoseMcpTap attribute names it.
	private const string PipePattern = "suisei-diag-{pid}";

	private static int _started;

	// Kept for the life of the process: the endpoint serves one host after another until the game exits.
	private static TapEndpoint? _endpoint;

	/// <summary>
	/// Start the endpoint, once per process; later calls do nothing. Does nothing while <see cref="Diagnostics.Enabled"/>
	/// is false, which it always is under NativeAOT. With <paramref name="tapDirectories"/>, a tap is loaded only from one
	/// of those folders or a folder beneath one; without, from any path the game can read. What the endpoint does goes to
	/// <see cref="Trace"/>.
	/// </summary>
	/// <returns>Whether this call started it.</returns>
	public static bool Start(IReadOnlyCollection<string>? tapDirectories = null)
	{
		if (!Diagnostics.Enabled || Interlocked.Exchange(ref _started, 1) != 0)
			return false;
		var version = typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
		_endpoint = TapEndpoint.Start(EndpointWire.PipeName(PipePattern, Environment.ProcessId), "SUIsei", version,
			static message => Trace.WriteLine(message, "SUIsei diagnostics"), tapDirectories);
		return true;
	}
}
