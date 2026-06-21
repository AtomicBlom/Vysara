namespace SUIsei.Controls;

/// <summary>The ordered child collection of a <see cref="Panel"/>; adds re-parent the child and invalidate the
/// owner's measure (PlanningUI.md §D2.1).</summary>
public sealed class UIElementCollection
{
	private readonly List<UIElement> _items = [];
	private readonly Panel _owner;

	internal UIElementCollection(Panel owner) => _owner = owner;

	public int Count => _items.Count;
	public UIElement this[int index] => _items[index];
	internal IReadOnlyList<UIElement> Items => _items;

	public void Add(UIElement child)
	{
		_items.Add(child);
		_owner.AdoptChild(child);
		_owner.InvalidateMeasure();
	}

	public void Clear()
	{
		foreach (var c in _items)
			_owner.DropChild(c);
		_items.Clear();
		_owner.InvalidateMeasure();
	}
}
