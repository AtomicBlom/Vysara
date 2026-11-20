namespace SUIsei.Controls;

/// <summary>An <see cref="Expander"/> has closed, as WinUI's <c>ExpanderCollapsedEventArgs</c>.</summary>
public sealed class ExpanderCollapsedEventArgs : EventArgs
{
	internal static readonly ExpanderCollapsedEventArgs Instance = new();
}
