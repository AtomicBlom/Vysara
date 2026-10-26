using System.Collections.ObjectModel;

namespace SUIsei.Media;

/// <summary>
/// A path geometry's figures, as WinUI's <c>PathFigureCollection</c>. A figure belongs to one collection at a time:
/// adding one that another collection holds throws, as WinUI does.
/// </summary>
public sealed class PathFigureCollection : Collection<PathFigure>, ISubPropertyListener
{
	private ListenerList _listeners;

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	void ISubPropertyListener.OnSubPropertyChanged(object source) => _listeners.Notify(source);

	protected override void InsertItem(int index, PathFigure item)
	{
		Adopt(item);
		base.InsertItem(index, item);
		_listeners.Notify(this);
	}

	protected override void SetItem(int index, PathFigure item)
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

	private void Adopt(PathFigure item)
	{
		ArgumentNullException.ThrowIfNull(item);
		if (item.IsOwned)
			throw new InvalidOperationException("The figure already belongs to a geometry; a figure can be in one figure collection at a time.");
		item.AddListener(this);
	}
}
