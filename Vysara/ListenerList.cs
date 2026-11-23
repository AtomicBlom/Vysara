namespace Vysara;

/// <summary>
/// The <see cref="ISubPropertyListener"/>s a shared sub-property object tells when it changes. A listener that shows the
/// object twice (an element using one brush for its background and its border) is added, told and removed once per use.
/// Telling them allocates nothing.
/// </summary>
internal struct ListenerList
{
	private List<ISubPropertyListener>? _items;

	/// <summary>How many uses are registered.</summary>
	public readonly int Count => _items?.Count ?? 0;

	public void Add(ISubPropertyListener listener) => (_items ??= []).Add(listener);

	public readonly void Remove(ISubPropertyListener listener) => _items?.Remove(listener);

	/// <summary>Tell every listener that <paramref name="source"/> changed. A listener may add or remove listeners while
	/// it is told.</summary>
	public readonly void Notify(object source)
	{
		if (_items is null)
			return;
		for (var i = 0; i < _items.Count; i++)
			_items[i].OnSubPropertyChanged(source);
	}
}
