using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Console;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BinaryVibrance.Vysara.Gallery;

/// <summary>
/// Where the gallery's text goes. A WinExe has no console of its own: its output reaches a terminal only through the
/// standard handles it was started with (a redirect, a pipe) or by attaching to the console of the terminal that started
/// it. Started any other way, from Explorer say, an error shows in a message box instead.
/// </summary>
internal static class ConsoleOutput
{
	private static bool _hasConsole;

	/// <summary>Use the standard error the gallery was started with, or attach to its parent's console. Call it before
	/// anything writes to the console, which binds to the handles there are when first written.</summary>
	public static void Connect()
	{
		var stderr = PInvoke.GetStdHandle(STD_HANDLE.STD_ERROR_HANDLE);
		_hasConsole = (stderr != HANDLE.Null && stderr != HANDLE.INVALID_HANDLE_VALUE) || PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS);
	}

	/// <summary>Report <paramref name="message"/> as an error: on standard error, or in a message box when there is none.</summary>
	public static void Error(string message)
	{
		if (_hasConsole)
			Console.Error.WriteLine(message);
		else
			PInvoke.MessageBox(HWND.Null, message, "Vysara gallery", MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
	}
}
