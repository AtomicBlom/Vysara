using System.Collections.ObjectModel;

namespace Vysara.Media;

/// <summary>
/// A figure's segments, in drawing order, as WinUI's <c>PathSegmentCollection</c>. A segment belongs to one collection
/// at a time: adding one that another collection holds throws, as WinUI does.
/// </summary>
public sealed class PathSegmentCollection : Collection<PathSegment>, ISubPropertyListener
{
	private ListenerList _listeners;

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	void ISubPropertyListener.OnSubPropertyChanged(object source) => _listeners.Notify(source);

	protected override void InsertItem(int index, PathSegment item)
	{
		Adopt(item);
		base.InsertItem(index, item);
		_listeners.Notify(this);
	}

	protected override void SetItem(int index, PathSegment item)
	{
		var old = this[index];
		if (ReferenceEquals(old, item))
			return;
		Adopt(item);
		old.RemoveListener(this);
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

	private void Adopt(PathSegment item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.IsOwned)
			throw new InvalidOperationException("The segment already belongs to a figure; a segment can be in one segment collection at a time.");
		item.AddListener(this);
	}
}
