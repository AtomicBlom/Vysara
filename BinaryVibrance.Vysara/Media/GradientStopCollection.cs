using System.Collections;

namespace BinaryVibrance.Vysara.Media;

/// <summary>
/// The stops of one <see cref="GradientBrush"/>, as WinUI's <c>GradientStopCollection</c>, in any order: the brush orders
/// them by offset. Adding, removing or replacing a stop, or changing one, updates the brush's paint in place. A stop may
/// sit in more than one brush's collection, and a change to it reaches each.
/// </summary>
public sealed class GradientStopCollection : IList<GradientStop>
{
	private readonly List<GradientStop> _items = [];
	private readonly GradientBrush _owner;

	internal GradientStopCollection(GradientBrush owner) => _owner = owner;

	public int Count => _items.Count;

	public bool IsReadOnly => false;

	public GradientStop this[int index]
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
			_owner.SyncStops();
		}
	}

	public void Add(GradientStop item) => Insert(_items.Count, item);

	public void Insert(int index, GradientStop item)
	{
		ArgumentNullException.ThrowIfNull(item);
		_items.Insert(index, item);
		item.AddListener(_owner);
		_owner.SyncStops();
	}

	public bool Remove(GradientStop item)
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
		_owner.SyncStops();
	}

	public void Clear()
	{
		foreach (var item in _items)
			item.RemoveListener(_owner);
		_items.Clear();
		_owner.SyncStops();
	}

	public bool Contains(GradientStop item) => _items.Contains(item);

	public int IndexOf(GradientStop item) => _items.IndexOf(item);

	public void CopyTo(GradientStop[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

	/// <summary>The stops in insertion order, for a struct enumerator.</summary>
	internal List<GradientStop> Items => _items;

	public List<GradientStop>.Enumerator GetEnumerator() => _items.GetEnumerator();

	IEnumerator<GradientStop> IEnumerable<GradientStop>.GetEnumerator() => _items.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
