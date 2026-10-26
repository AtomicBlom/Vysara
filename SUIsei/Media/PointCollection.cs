using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace SUIsei.Media;

/// <summary>
/// An ordered list of points, as WinUI's <c>PointCollection</c>: the vertices of a poly segment (and of a polyline or
/// polygon). Every change is reported to the geometry or element using it. In XAML it is written as pairs of numbers,
/// <c>Points="0,0 10,10 20,0"</c>.
/// </summary>
public sealed class PointCollection : Collection<Point>
{
	private readonly List<Point> _items;
	private ListenerList _listeners;

	public PointCollection() : this([])
	{
	}

	private PointCollection(List<Point> items) : base(items) => _items = items;

	/// <summary>
	/// Replace every point at once, reusing the collection's storage, and report one change. SUIsei-only and code-only:
	/// the way to animate a polyline or a chart series every frame without allocating, where setting points one at a
	/// time would report a change per point.
	/// </summary>
	public void SetAll(ReadOnlySpan<Point> points)
	{
		_items.Clear();
		_items.AddRange(points);
		_listeners.Notify(this);
	}

	/// <summary>The points, over the live storage: valid until the collection next changes.</summary>
	internal ReadOnlySpan<Point> AsSpan() => CollectionsMarshal.AsSpan(_items);

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	protected override void InsertItem(int index, Point item)
	{
		base.InsertItem(index, item);
		_listeners.Notify(this);
	}

	protected override void SetItem(int index, Point item)
	{
		base.SetItem(index, item);
		_listeners.Notify(this);
	}

	protected override void RemoveItem(int index)
	{
		base.RemoveItem(index);
		_listeners.Notify(this);
	}

	protected override void ClearItems()
	{
		base.ClearItems();
		_listeners.Notify(this);
	}
}
