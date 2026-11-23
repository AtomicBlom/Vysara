using System.Collections.ObjectModel;

namespace BinaryVibrance.Vysara.Media;

/// <summary>A geometry group's children, as WinUI's <c>GeometryCollection</c>. A geometry may be in several
/// collections, and in one more than once.</summary>
public sealed class GeometryCollection : Collection<Geometry>, ISubPropertyListener
{
	private ListenerList _listeners;

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	void ISubPropertyListener.OnSubPropertyChanged(object source) => _listeners.Notify(source);

	// A group whose children are this collection, or hold a group whose are, would be inside its own group: building or
	// notifying it would never end.
	private void ThrowIfReachesThis(Geometry item)
	{
		if (item is GeometryGroup group && group.GetValue(GeometryGroup.ChildrenProperty) is { } children
			&& Reaches(children, null, this, []))
			throw new ArgumentException("A GeometryGroup can't hold itself, directly or through a group inside it.", nameof(item));
	}

	/// <summary>
	/// Whether <paramref name="children"/>, or the children of a group inside it at any depth, holds
	/// <paramref name="group"/> or is <paramref name="collection"/>. Each collection is walked once, in
	/// <paramref name="visited"/>, so geometry shared down many branches costs no more than geometry used once.
	/// </summary>
	internal static bool Reaches(GeometryCollection children, GeometryGroup? group, GeometryCollection? collection,
		HashSet<GeometryCollection> visited)
	{
		if (ReferenceEquals(children, collection))
			return true;
		if (!visited.Add(children))
			return false;
		foreach (var child in children)
		{
			if (ReferenceEquals(child, group))
				return true;
			if (child is GeometryGroup inner && inner.GetValue(GeometryGroup.ChildrenProperty) is { } innerChildren
				&& Reaches(innerChildren, group, collection, visited))
				return true;
		}
		return false;
	}

	protected override void InsertItem(int index, Geometry item)
	{
		ArgumentNullException.ThrowIfNull(item);
		ThrowIfReachesThis(item);
		item.AddListener(this);
		base.InsertItem(index, item);
		_listeners.Notify(this);
	}

	protected override void SetItem(int index, Geometry item)
	{
		ArgumentNullException.ThrowIfNull(item);
		ThrowIfReachesThis(item);
		this[index].RemoveListener(this);
		item.AddListener(this);
		base.SetItem(index, item);
		_listeners.Notify(this);
	}

	protected override void RemoveItem(int index)
	{
		this[index].RemoveListener(this);
		base.RemoveItem(index);
		_listeners.Notify(this);
	}

	protected override void ClearItems()
	{
		for (var i = 0; i < Count; i++)
			this[i].RemoveListener(this);
		base.ClearItems();
		_listeners.Notify(this);
	}
}
