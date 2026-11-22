namespace SUIsei.Controls;

/// <summary>The ordered child collection of a <see cref="Panel"/>; adds re-parent the child and invalidate the
/// owner's measure.</summary>
public sealed class UIElementCollection
{
	private readonly List<UIElement> _items = [];
	private readonly Panel _owner;

	internal UIElementCollection(Panel owner) => _owner = owner;

	public int Count => _items.Count;
	public UIElement this[int index] => _items[index];
	// The children, typed as the List they are so a panel's layout loop gets List's struct enumerator: a foreach over
	// IReadOnlyList<T> boxes one on every measure and arrange. Changes go through the methods below, which keep
	// parenting and invalidation in step.
	internal List<UIElement> Items => _items;

	public void Add(UIElement child)
	{
		_items.Add(child);
		_owner.AdoptChild(child);
		_owner.InvalidateMeasure();
	}

	/// <summary>Insert <paramref name="child"/> at <paramref name="index"/> (used by items controls applying an
	/// incremental collection change).</summary>
	public void Insert(int index, UIElement child)
	{
		_items.Insert(index, child);
		_owner.AdoptChild(child);
		_owner.InvalidateMeasure();
	}

	/// <summary>Remove the child at <paramref name="index"/>, un-parenting it.</summary>
	public void RemoveAt(int index)
	{
		var child = _items[index];
		_items.RemoveAt(index);
		_owner.DropChild(child);
		_owner.InvalidateMeasure();
	}

	/// <summary>Where <paramref name="child"/> is in the collection, or -1.</summary>
	public int IndexOf(UIElement child) => _items.IndexOf(child);

	/// <summary>Whether <paramref name="child"/> is in the collection.</summary>
	public bool Contains(UIElement child) => _items.Contains(child);

	/// <summary>Remove <paramref name="child"/>, un-parenting it; false when it isn't in the collection.</summary>
	public bool Remove(UIElement child)
	{
		var index = _items.IndexOf(child);
		if (index < 0)
			return false;
		RemoveAt(index);
		return true;
	}

	public void Clear()
	{
		foreach (var c in _items)
			_owner.DropChild(c);
		_items.Clear();
		_owner.InvalidateMeasure();
	}
}
