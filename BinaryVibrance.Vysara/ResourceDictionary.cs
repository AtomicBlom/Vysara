namespace BinaryVibrance.Vysara;

/// <summary>
/// A keyed resource store used by <see cref="FrameworkElement.Resources"/> and resolved at compile time by the
/// XAML source generator for <c>{StaticResource key}</c> references. Runtime lookup walks
/// <see cref="MergedDictionaries"/> in reverse-index order (later entries take priority).
/// </summary>
public sealed class ResourceDictionary
{
	private readonly Dictionary<string, object?> _items = [];

	public void Add(string key, object? value) => _items[key] = value;
	public bool TryGetValue(string key, out object? value) => _items.TryGetValue(key, out value);
	public object? this[string key]
	{
		get => _items[key];
		set => _items[key] = value;
	}

	/// <summary>Additional dictionaries merged into this one. Later entries take priority over earlier ones.</summary>
	public IList<ResourceDictionary> MergedDictionaries { get; } = [];

	/// <summary>Look up <paramref name="key"/> in this dictionary then its merged dictionaries (depth-first).</summary>
	public bool TryFind(string key, out object? value)
	{
		if (_items.TryGetValue(key, out value))
			return true;
		for (var i = MergedDictionaries.Count - 1; i >= 0; i--)
		{
			if (MergedDictionaries[i].TryFind(key, out value))
				return true;
		}
		value = null;
		return false;
	}
}
