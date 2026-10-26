using System.Collections;

namespace SUIsei.Media;

/// <summary>The children of a <see cref="TransformGroup"/>, as WinUI's <c>TransformCollection</c>. Adding, removing or
/// replacing a child, or changing one, moves every element showing the group.</summary>
public sealed class TransformCollection : IList<Transform>
{
	private readonly List<Transform> _items = [];
	private readonly TransformGroup _owner;

	internal TransformCollection(TransformGroup owner) => _owner = owner;

	public int Count => _items.Count;

	public bool IsReadOnly => false;

	public Transform this[int index]
	{
		get => _items[index];
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			var old = _items[index];
			if (ReferenceEquals(old, value))
				return;
			old.RemoveListener(_owner);
			_items[index] = value;
			value.AddListener(_owner);
			_owner.ChildrenChanged();
		}
	}

	public void Add(Transform item) => Insert(_items.Count, item);

	public void Insert(int index, Transform item)
	{
		ArgumentNullException.ThrowIfNull(item);
		_items.Insert(index, item);
		item.AddListener(_owner);
		_owner.ChildrenChanged();
	}

	public bool Remove(Transform item)
	{
		var index = _items.IndexOf(item);
		if (index < 0)
			return false;
		RemoveAt(index);
		return true;
	}

	public void RemoveAt(int index)
	{
		var item = _items[index];
		_items.RemoveAt(index);
		item.RemoveListener(_owner);
		_owner.ChildrenChanged();
	}

	public void Clear()
	{
		foreach (var item in _items)
			item.RemoveListener(_owner);
		_items.Clear();
		_owner.ChildrenChanged();
	}

	public bool Contains(Transform item) => _items.Contains(item);

	public int IndexOf(Transform item) => _items.IndexOf(item);

	public void CopyTo(Transform[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

	/// <summary>The children in order, for an indexed walk.</summary>
	internal List<Transform> Items => _items;

	public List<Transform>.Enumerator GetEnumerator() => _items.GetEnumerator();

	IEnumerator<Transform> IEnumerable<Transform>.GetEnumerator() => _items.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
