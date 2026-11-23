using System.Globalization;

namespace BinaryVibrance.Vysara.RoseTap;

/// <summary>
/// Where the tap says what it did: <c>vysara-rosetap.log</c> in the work folder the host named, or in the temp folder
/// before the startup data has named one, so a tap that fails to start still says why. Each line is appended and the
/// file closed again, so nothing is held open in a game that keeps the tap loaded between sessions. Never throws: a
/// log that cannot be written is not a reason to fail a command.
/// </summary>
internal sealed class TapLog
{
	private const string FileName = "vysara-rosetap.log";

	private readonly Lock _gate = new();

	private TapLog(string path) => Path = path;

	/// <summary>The file this log appends to.</summary>
	public string Path { get; }

	/// <summary>A log in <paramref name="workDir"/>, created where it does not exist yet.</summary>
	public static TapLog In(string workDir)
	{
		try
		{
			Directory.CreateDirectory(workDir);
			return new TapLog(System.IO.Path.Combine(workDir, FileName));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			var fallback = InTemp();
			fallback.Write($"Could not log to {workDir}: {exception.Message}");
			return fallback;
		}
	}

	/// <summary>A log in the temp folder, for what happens before a work folder is known.</summary>
	public static TapLog InTemp() => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), FileName));

	/// <summary>Append one line, stamped with the time and the thread that wrote it.</summary>
	public void Write(string message)
	{
		var line = string.Create(CultureInfo.InvariantCulture,
			$"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
		lock (_gate)
		{
			try
			{
				File.AppendAllText(Path, line);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}
