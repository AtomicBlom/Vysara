namespace SUIsei.Controls;

/// <summary>
/// Defines the visual tree for a data item (PlanningUI.md §D1.6) — used by <see cref="ContentPresenter"/> and items
/// controls to realize one subtree per item. The <see cref="Factory"/> <c>new</c>s the subtree given the item and
/// wires its template-scoped <c>{x:Bind}</c>s against the item.
/// </summary>
public sealed class DataTemplate : FrameworkTemplate
{
	/// <summary>Builds the element tree for one data item.</summary>
	public required Func<object?, FrameworkElement> Factory { get; init; }
}
