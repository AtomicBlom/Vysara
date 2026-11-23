using RoseMcp.Tap.Protocol;

[assembly: RoseMcpTap(Name = "Vysara", Module = "BinaryVibrance.Vysara.dll", Endpoint = "vysara-diag-{pid}", EntryType = "BinaryVibrance.Vysara.RoseTap.Tap", Protocol = TapGreeting.ProtocolVersion)]

namespace BinaryVibrance.Vysara.RoseTap;

/// <summary>
/// The entry point Vysara's diagnostics endpoint calls when a RoseMCP host loads this tap into a game. It is found by
/// name, as the contract's <c>RoseMcpTap</c> attribute says, so its two methods are the whole of what the endpoint
/// needs: <see cref="Start"/> begins a session on the host's pipe, and <see cref="Stop"/> ends it.
/// <para>
/// One session at a time. A second <see cref="Start"/> is a host reconnecting, so the session it replaces is ended
/// first.
/// </para>
/// </summary>
public static class Tap
{
	private static readonly Lock Gate = new();
	private static TapSession? _session;

	/// <summary>
	/// Begin a session with the host named in <paramref name="startupData"/>, <c>workDir|pipeName|key</c>: the folder
	/// the tap logs to, the host's pipe, and the key the greeting presents. Returns once the session's reader thread is
	/// running; connecting, greeting and serving happen there, so the endpoint's answer to the host is never held up.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="startupData"/> is not <c>workDir|pipeName|key</c>.</exception>
	public static void Start(string startupData)
	{
		if (!TryParse(startupData, out var workDir, out var pipeName, out var key))
		{
			var reason = $"The startup data '{startupData}' is not workDir|pipeName|key.";
			TapLog.InTemp().Write(reason);
			throw new ArgumentException(reason, nameof(startupData));
		}

		lock (Gate)
		{
			_session?.End();
			_session = new TapSession(workDir, pipeName, key, TapLog.In(workDir));
			_session.Begin();
		}
	}

	/// <summary>
	/// End the session, if any: close the host's pipe and drop everything the tap holds in the game, so the load context
	/// the endpoint loaded it into can be unloaded.
	/// </summary>
	public static void Stop()
	{
		lock (Gate)
		{
			_session?.End();
			_session = null;
		}
	}

	// Split from the right: the pipe name and the key never hold a '|', and a work folder could.
	private static bool TryParse(string? startupData, out string workDir, out string pipeName, out string key)
	{
		var parts = (startupData ?? "").Split('|');
		workDir = parts.Length >= 3 ? string.Join('|', parts[..^2]) : "";
		pipeName = parts.Length >= 3 ? parts[^2] : "";
		key = parts.Length >= 3 ? parts[^1] : "";
		return workDir.Length > 0 && pipeName.Length > 0 && key.Length > 0;
	}
}
