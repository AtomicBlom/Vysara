namespace Vysara.Controls;

/// <summary>An <see cref="InfoBar"/> has closed, as WinUI's <c>InfoBarClosedEventArgs</c>.</summary>
public sealed class InfoBarClosedEventArgs(InfoBarCloseReason reason) : EventArgs
{
	/// <summary>What closed it.</summary>
	public InfoBarCloseReason Reason { get; } = reason;
}
