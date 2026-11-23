namespace Vysara.Controls;

/// <summary>An <see cref="Expander"/> is opening, as WinUI's <c>ExpanderExpandingEventArgs</c>.</summary>
public sealed class ExpanderExpandingEventArgs : EventArgs
{
	internal static readonly ExpanderExpandingEventArgs Instance = new();
}
