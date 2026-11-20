using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>An item clicked or invoked in a <see cref="ListView"/>, as WinUI's <c>ItemClickEventArgs</c>.</summary>
public sealed class ItemClickEventArgs(object? clickedItem) : RoutedEventArgs
{
	/// <summary>The data item clicked.</summary>
	public object? ClickedItem { get; } = clickedItem;
}
