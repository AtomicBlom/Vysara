using System.Diagnostics.CodeAnalysis;

namespace BinaryVibrance.Vysara;

/// <summary>
/// The fonts a host's text may name by <see cref="Media.FontFamily"/>: each an atlas under a name, ignoring case
/// (<see cref="UIHost.Fonts"/>). Text whose family names none of them draws in its root's <see cref="UIRoot.Font"/>.
/// Setting a name to another atlas, or removing it, re-measures the text of every root the host has, so a name can stand
/// for a role that an app gives a new font while it runs (a theme's).
/// </summary>
public sealed class FontCollection
{
	private readonly Dictionary<string, FontAtlas> _fonts = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Raised after a name's atlas changed, or the name was removed.</summary>
	internal event Action? Changed;

	/// <summary>The names held.</summary>
	public IEnumerable<string> Names => _fonts.Keys;

	/// <summary>How many fonts are held.</summary>
	public int Count => _fonts.Count;

	/// <summary>Make <paramref name="name"/> stand for <paramref name="font"/>; text in that family measures again with it.</summary>
	public void Set(string name, FontAtlas font)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(font);
		name = name.Trim();
		if (_fonts.TryGetValue(name, out var held) && ReferenceEquals(held, font))
			return;
		_fonts[name] = font;
		Changed?.Invoke();
	}

	/// <summary>Let go of <paramref name="name"/>: text in that family draws in its root's font. False when it wasn't held.</summary>
	public bool Remove(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!_fonts.Remove(name.Trim()))
			return false;
		Changed?.Invoke();
		return true;
	}

	/// <summary>The atlas <paramref name="name"/> stands for, if it is held.</summary>
	public bool TryGet(string name, [NotNullWhen(true)] out FontAtlas? font)
	{
		ArgumentNullException.ThrowIfNull(name);
		return _fonts.TryGetValue(name.Trim(), out font);
	}
}
