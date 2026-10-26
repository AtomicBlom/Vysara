using System.Collections.ObjectModel;

namespace SUIsei.Media;

/// <summary>A geometry group's children, as WinUI's <c>GeometryCollection</c>. A geometry may be in several
/// collections, and in one more than once.</summary>
public sealed class GeometryCollection : Collection<Geometry>, ISubPropertyListener
{
	private ListenerList _listeners;

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	void ISubPropertyListener.OnSubPropertyChanged(object source) => _listeners.Notify(source);

	protected override void InsertItem(int index, Geometry item)
	{
		ArgumentNullException.ThrowIfNull(item);
		item.AddListener(this);
		base.InsertItem(index, item);
		_listeners.Notify(this);
	}

	protected override void SetItem(int index, Geometry item)
	{
		ArgumentNullException.ThrowIfNull(item);
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
