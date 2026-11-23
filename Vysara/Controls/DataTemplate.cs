namespace Vysara.Controls;

/// <summary>
/// Defines the visual tree for a data item — used by <see cref="ContentPresenter"/> and items
/// controls to realize one subtree per item. The <see cref="Factory"/> <c>new</c>s the subtree given the item and
/// wires its template-scoped <c>{x:Bind}</c>s against the item.
/// </summary>
public sealed class DataTemplate : FrameworkTemplate
{
	/// <summary>Builds the element tree for one data item.</summary>
	public required Func<object?, FrameworkElement> Factory { get; init; }

	/// <summary>
	/// Keeps a realized template's live <c>{x:Bind}</c>s with <paramref name="element"/>, the root it built: they listen to the
	/// item while the element is in a live tree and stop when it leaves, which is how a container an items control drops,
	/// recycles or replaces lets go of its item. Generated template factories call it; an element keeps one set, a later
	/// call replacing the earlier.
	/// </summary>
	public static void SetBindings(FrameworkElement element, IDataTemplateBindings bindings)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(bindings);
		element.SetTemplateBindings(bindings);
	}
}
