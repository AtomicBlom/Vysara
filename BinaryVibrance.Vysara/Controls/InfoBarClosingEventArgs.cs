namespace BinaryVibrance.Vysara.Controls;

/// <summary>An <see cref="InfoBar"/> is about to close, as WinUI's <c>InfoBarClosingEventArgs</c>: setting
/// <see cref="Cancel"/> keeps it open.</summary>
public sealed class InfoBarClosingEventArgs(InfoBarCloseReason reason) : EventArgs
{
	/// <summary>What is closing it.</summary>
	public InfoBarCloseReason Reason { get; } = reason;

	/// <summary>Set true to keep the bar open.</summary>
	public bool Cancel { get; set; }
}
