namespace SUIsei.Controls;

/// <summary>
/// The live <c>{x:Bind}</c>s (<c>Mode=OneWay</c> or <c>TwoWay</c>) of one element a <see cref="DataTemplate"/> built,
/// tracking its item. The generator emits one per realized template that has them and hands it to
/// <see cref="DataTemplate.SetBindings"/>; it has pushed the item's values once already, and listens to the item only
/// while the element is in a live tree, so a container an items control drops, recycles or replaces lets its item go.
/// </summary>
public interface IDataTemplateBindings
{
	/// <summary>The element joined a live tree: subscribe to the item's changes and push its values again, since they may
	/// have changed while it was away.</summary>
	void Attach();

	/// <summary>The element left the live tree: stop listening to the item.</summary>
	void Detach();
}
