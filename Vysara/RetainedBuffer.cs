namespace Vysara;

/// <summary>
/// A growable array that keeps its storage when cleared: the backing store of a stream rebuilt every frame. It grows
/// by doubling, so a stream that settles at a steady size stops allocating, and its contents are handed out as
/// <see cref="ReadOnlyMemory{T}"/> over the live array rather than copied.
/// </summary>
internal sealed class RetainedBuffer<T>(int initialCapacity)
	where T : unmanaged
{
	private T[] _items = new T[initialCapacity];

	/// <summary>How many items have been added since the last <see cref="Clear"/>.</summary>
	public int Count { get; private set; }

	/// <summary>The added items, over the live array: valid until the buffer is next cleared or grows.</summary>
	public ReadOnlyMemory<T> Memory => _items.AsMemory(0, Count);

	/// <summary>The added items, over the live array: valid until the buffer is next cleared or grows.</summary>
	public ReadOnlySpan<T> Span => _items.AsSpan(0, Count);

	/// <summary>The added items, writable in place, over the live array: valid until the buffer is next cleared or grows.</summary>
	public Span<T> MutableSpan => _items.AsSpan(0, Count);

	/// <summary>Forget the contents but keep the storage. The items are unmanaged, so stale ones hold nothing alive.</summary>
	public void Clear() => Count = 0;

	/// <summary>An added item, by reference, to rewrite in place.</summary>
	public ref T this[int index]
	{
		get
		{
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
			return ref _items[index];
		}
	}

	/// <summary>Keep only the first <paramref name="count"/> items, keeping the storage.</summary>
	public void Truncate(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Count);
		Count = count;
	}

	public void Add(in T item)
	{
		if (Count == _items.Length)
			Array.Resize(ref _items, Math.Max(_items.Length * 2, 16));
		_items[Count++] = item;
	}
}
