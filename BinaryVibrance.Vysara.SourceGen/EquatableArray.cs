namespace BinaryVibrance.Vysara.SourceGen;

/// <summary>
/// A value-equatable wrapper over an array, so an incremental-generator model that contains a collection still
/// compares by <em>contents</em>. Without this, a record holding a raw array compares the array by reference, so
/// the pipeline node is considered changed on every run and the generator never caches — the classic
/// "incremental in name only" trap. Treats a null array and an empty array as equal.
/// </summary>
internal readonly struct EquatableArray<T>(T[]? array) : IEquatable<EquatableArray<T>> where T : IEquatable<T>
{
	private readonly T[]? _array = array;

	public int Count => _array?.Length ?? 0;

	public T[] AsArray() => _array ?? [];

	public bool Equals(EquatableArray<T> other)
	{
		var a = _array;
		var b = other._array;
		if (a is null || a.Length == 0)
			return b is null || b.Length == 0;
		if (b is null || a.Length != b.Length)
			return false;
		for (var i = 0; i < a.Length; i++)
			if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
				return false;
		return true;
	}

	public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

	public override int GetHashCode()
	{
		if (_array is null)
			return 0;
		var hash = 17;
		foreach (var item in _array)
			hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
		return hash;
	}

	public static implicit operator EquatableArray<T>(T[]? array) => new(array);
}
