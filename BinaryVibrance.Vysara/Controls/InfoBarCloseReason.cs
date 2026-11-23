namespace BinaryVibrance.Vysara.Controls;

/// <summary>What closed an <see cref="InfoBar"/>, as WinUI's <c>InfoBarCloseReason</c>.</summary>
public enum InfoBarCloseReason
{
	/// <summary>Its close button was clicked.</summary>
	CloseButton,

	/// <summary><see cref="InfoBar.IsOpen"/> was set false.</summary>
	Programmatic,
}
