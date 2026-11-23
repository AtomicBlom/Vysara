using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Vysara.Media;

/// <summary>
/// An ordered list of numbers, as WinUI's <c>DoubleCollection</c>: a shape's dash pattern, for one. Every change is
/// reported to the element using it. In XAML it is written as numbers separated by commas or spaces,
/// <c>StrokeDashArray="2 1"</c>.
/// </summary>
public sealed class DoubleCollection : Collection<double>
{
	private readonly List<double> _items;
	private ListenerList _listeners;

	public DoubleCollection() : this([])
	{
	}

	private DoubleCollection(List<double> items) : base(items) => _items = items;

	/// <summary>Replace every number at once, reusing the collection's storage, and report one change. Vysara-only and
	/// code-only, like <see cref="PointCollection.SetAll"/>.</summary>
	public void SetAll(ReadOnlySpan<double> values)
	{
		_items.Clear();
		_items.AddRange(values);
		_listeners.Notify(this);
	}

	/// <summary>The numbers, over the live storage: valid until the collection next changes.</summary>
	internal ReadOnlySpan<double> AsSpan() => CollectionsMarshal.AsSpan(_items);

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	protected override void InsertItem(int index, double item)
	{
		base.InsertItem(index, item);
		_listeners.Notify(this);
	}

	protected override void SetItem(int index, double item)
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
