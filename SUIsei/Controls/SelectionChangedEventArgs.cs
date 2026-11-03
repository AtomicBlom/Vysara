using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>What a selection change took away and added, as WinUI's <c>SelectionChangedEventArgs</c>: the items
/// deselected and the items selected, in the order they changed.</summary>
public sealed class SelectionChangedEventArgs(IList<object?> removedItems, IList<object?> addedItems) : RoutedEventArgs
{
	/// <summary>The items no longer selected.</summary>
	public IList<object?> RemovedItems { get; } = removedItems;

	/// <summary>The items newly selected.</summary>
	public IList<object?> AddedItems { get; } = addedItems;
}
