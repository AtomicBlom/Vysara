namespace BinaryVibrance.Vysara;

/// <summary>
/// A keyed resource store used by <see cref="FrameworkElement.Resources"/> and resolved at compile time by the
/// XAML source generator for <c>{StaticResource key}</c> references. Runtime lookup walks
/// <see cref="MergedDictionaries"/> in reverse-index order (later entries take priority).
/// </summary>
public sealed class ResourceDictionary
{
	private readonly Dictionary<string, object?> _items = [];
	private Dictionary<Type, Style>? _implicitStyles;

	// How many implicit styles any dictionary holds: none, and elements joining a tree look for none
	private static int s_implicitStyles;

	internal static bool AnyImplicitStyles => Volatile.Read(ref s_implicitStyles) > 0;

	public void Add(string key, object? value) => _items[key] = value;
	public bool TryGetValue(string key, out object? value) => _items.TryGetValue(key, out value);
	public object? this[string key]
	{
		get => _items[key];
		set => _items[key] = value;
	}

	/// <summary>
	/// Keep <paramref name="style"/> with no key, as the implicit style of its <see cref="Style.TargetType"/>, as a WinUI
	/// style with a <c>TargetType</c> and no <c>x:Key</c> is: every element of exactly that type under this dictionary's
	/// owner that has no <c>Style</c> of its own takes it as it joins the tree. A later one for the same type replaces it.
	/// </summary>
	public void Add(Style style)
	{
		ArgumentNullException.ThrowIfNull(style);
		var type = style.TargetType ?? throw new ArgumentException("An implicit style needs a TargetType.", nameof(style));
		_implicitStyles ??= [];
		if (!_implicitStyles.ContainsKey(type))
			Interlocked.Increment(ref s_implicitStyles);
		_implicitStyles[type] = style;
	}

	/// <summary>The implicit style for exactly <paramref name="type"/> in this dictionary, then its merged dictionaries
	/// (the later first).</summary>
	internal bool TryFindImplicitStyle(Type type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Style? style)
	{
		if (_implicitStyles is not null && _implicitStyles.TryGetValue(type, out style))
			return true;
		for (var i = MergedDictionaries.Count - 1; i >= 0; i--)
		{
			if (MergedDictionaries[i].TryFindImplicitStyle(type, out style))
				return true;
		}
		style = null;
		return false;
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
