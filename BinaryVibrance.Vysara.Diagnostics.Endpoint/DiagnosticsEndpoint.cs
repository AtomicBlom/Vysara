using System.Diagnostics;
using System.Reflection;
using RoseMcp.Tap.Endpoint;
using RoseMcp.Tap.Protocol;

namespace BinaryVibrance.Vysara;

/// <summary>
/// The diagnostics endpoint, on the pipe <c>vysara-diag-{pid}</c>, through which a RoseMCP host loads a tap such as
/// BinaryVibrance.Vysara.RoseTap into a running game, whether it launched the game or attached to it later. A tap reaches the game
/// through <see cref="Diagnostics"/> alone.
/// <para>
/// A game opts in twice: it turns <see cref="Diagnostics"/> on in its runtime config, and it calls <see cref="Start"/>.
/// The endpoint loads an assembly a client on the pipe names and runs it, so a game that only wants the in-process
/// <see cref="Diagnostics"/> API, for an overlay of its own, never calls <see cref="Start"/>. The pipe accepts only the
/// game's own user, and a tap loads only from the folders <see cref="Start"/> is given or, by default, the folders
/// RoseMCP finds taps in (<see cref="DefaultTapDirectories()"/>), never from any path the game can read.
/// </para>
/// </summary>
public static class DiagnosticsEndpoint
{
	// The pipe the endpoint listens on, as BinaryVibrance.Vysara.RoseTap's RoseMcpTap attribute names it.
	private const string PipePattern = "vysara-diag-{pid}";

	private static int _started;

	// Kept for the life of the process: the endpoint serves one host after another until the game exits.
	private static TapEndpoint? _endpoint;

	/// <summary>
	/// Start the endpoint, once per process; later calls do nothing. Does nothing while <see cref="Diagnostics.Enabled"/>
	/// is false, which it always is under NativeAOT. A tap is loaded only from one of <paramref name="tapDirectories"/>
	/// or a folder beneath one, judged by where symbolic links and junctions lead; given none, from
	/// <see cref="DefaultTapDirectories()"/>, the folders RoseMCP itself finds taps in. What the endpoint does goes to
	/// <see cref="Trace"/>.
	/// </summary>
	/// <returns>Whether this call started it.</returns>
	public static bool Start(IReadOnlyCollection<string>? tapDirectories = null)
	{
		if (!Diagnostics.Enabled || Interlocked.Exchange(ref _started, 1) != 0)
			return false;
		var folders = tapDirectories is { Count: > 0 } ? tapDirectories : DefaultTapDirectories();
		Trace.WriteLine(folders.Count > 0 ? $"Taps load only from {string.Join(", ", folders)}." : "No folder to load taps from, so none will load.",
			"Vysara diagnostics");
		var version = typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
		_endpoint = TapEndpoint.Start(EndpointWire.PipeName(PipePattern, Environment.ProcessId), "Vysara", version,
			static message => Trace.WriteLine(message, "Vysara diagnostics"), folders);
		return true;
	}

	/// <summary>The environment variable naming more folders RoseMCP finds taps in, separated as a PATH is.</summary>
	public const string TapsVariable = "ROSEMCP_TAPS";

	/// <summary>
	/// The folders <see cref="Start"/> loads taps from when given none, the ones RoseMCP finds taps in: the <c>taps</c> folder
	/// beside RoseMCP's settings, <c>%LOCALAPPDATA%/BinaryVibrance/RoseMCP/taps</c> (local application data as .NET finds it on
	/// each platform, <c>~/.local/share</c> on Linux), where tools/deploy-tap.ps1 deploys to, wherever RoseMCP itself is
	/// installed; then each folder <c>ROSEMCP_TAPS</c> names, separated as a PATH is. An entry of <c>ROSEMCP_TAPS</c> that is
	/// not a full path is left out, since the game and RoseMCP would each read it against a folder of their own. The folders
	/// RoseMCP's settings name are not read; a game that loads taps from one passes it to <see cref="Start"/> along with these.
	/// </summary>
	public static IReadOnlyList<string> DefaultTapDirectories() =>
		DefaultTapDirectories(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

	// The folders above, from the environment and local application data given, so a test can give its own.
	internal static IReadOnlyList<string> DefaultTapDirectories(Func<string, string?> environment, string localAppData)
	{
		var folders = new List<string>();
		if (localAppData.Length > 0)
			Add(Path.Combine(localAppData, "BinaryVibrance", "RoseMCP", "taps"));
		foreach (var named in (environment(TapsVariable) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (Path.IsPathFullyQualified(named))
				Add(named);
		}
		return folders;

		void Add(string folder)
		{
			var full = Path.GetFullPath(folder);
			if (!folders.Contains(full, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
				folders.Add(full);
		}
	}
}
