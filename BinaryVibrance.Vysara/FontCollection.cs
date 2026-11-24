using System.Diagnostics.CodeAnalysis;

namespace BinaryVibrance.Vysara;

/// <summary>
/// The fonts a host's text may name by <see cref="Media.FontFamily"/> (<see cref="UIHost.Fonts"/>): under each family's
/// name, ignoring case, an atlas for each <see cref="FontWeight"/> it has. Text asks for its family and weight and gets
/// that weight, or the nearest the family has, as CSS chooses: from 400 to 500, up to 500 first, then down, then up; under
/// 400, down first, then up; over 500, up first, then down. Text whose family names none of them draws in its root's
/// <see cref="UIRoot.Font"/>. Setting a font, or removing one, re-measures the text of every root the host has, so a name
/// can stand for a role that an app gives new fonts while it runs (a theme's).
/// </summary>
public sealed class FontCollection
{
	private readonly Dictionary<string, SortedList<ushort, FontAtlas>> _fonts = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Raised after a font was set to another atlas, or removed.</summary>
	internal event Action? Changed;

	/// <summary>The family names held.</summary>
	public IEnumerable<string> Names => _fonts.Keys;

	/// <summary>How many families are held.</summary>
	public int Count => _fonts.Count;

	/// <summary>Make <paramref name="name"/>'s normal weight <paramref name="font"/>.</summary>
	public void Set(string name, FontAtlas font) => Set(name, FontWeights.Normal, font);

	/// <summary>Make <paramref name="name"/>'s <paramref name="weight"/> <paramref name="font"/>; text in that family
	/// measures again with whichever of its fonts suits it.</summary>
	public void Set(string name, FontWeight weight, FontAtlas font)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(font);
		name = name.Trim();
		if (!_fonts.TryGetValue(name, out var weights))
			_fonts[name] = weights = [];
		else if (weights.TryGetValue(weight.Weight, out var held) && ReferenceEquals(held, font))
			return;
		weights[weight.Weight] = font;
		Changed?.Invoke();
	}

	/// <summary>Let go of every font <paramref name="name"/> has: text in that family draws in its root's font. False when
	/// it had none.</summary>
	public bool Remove(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (!_fonts.Remove(name.Trim()))
			return false;
		Changed?.Invoke();
		return true;
	}

	/// <summary>Let go of <paramref name="name"/>'s <paramref name="weight"/>; text asking for it takes the nearest it
	/// still has. False when it didn't have it.</summary>
	public bool Remove(string name, FontWeight weight)
	{
		ArgumentNullException.ThrowIfNull(name);
		name = name.Trim();
		if (!_fonts.TryGetValue(name, out var weights) || !weights.Remove(weight.Weight))
			return false;
		if (weights.Count == 0)
			_fonts.Remove(name);
		Changed?.Invoke();
		return true;
	}

	/// <summary>The atlas <paramref name="name"/>'s normal weight is drawn in, if the family is held.</summary>
	public bool TryGet(string name, [NotNullWhen(true)] out FontAtlas? font) => TryGet(name, FontWeights.Normal, out font);

	/// <summary>The atlas text in <paramref name="name"/> at <paramref name="weight"/> is drawn in: that weight's, or the
	/// nearest the family has; false when the family isn't held.</summary>
	public bool TryGet(string name, FontWeight weight, [NotNullWhen(true)] out FontAtlas? font)
	{
		ArgumentNullException.ThrowIfNull(name);
		font = null;
		if (!_fonts.TryGetValue(name.Trim(), out var weights) || weights.Count == 0)
			return false;
		font = weights.GetValueAtIndex(Nearest(weights.Keys, weight.Weight));
		return true;
	}

	// The index of the weight CSS's font matching takes for `wanted` among `held` (ascending, not empty)
	private static int Nearest(IList<ushort> held, ushort wanted)
	{
		var exact = -1;
		for (var i = 0; i < held.Count; i++)
			if (held[i] == wanted)
				exact = i;
		if (exact >= 0)
			return exact;
		int Lighter() { for (var i = held.Count - 1; i >= 0; i--) if (held[i] < wanted) return i; return -1; }
		int Heavier(int upTo = 1000) { for (var i = 0; i < held.Count; i++) if (held[i] > wanted && held[i] <= upTo) return i; return -1; }
		if (wanted is >= 400 and <= 500)
		{
			var upTo500 = Heavier(500);
			if (upTo500 >= 0)
				return upTo500;
			var lighter = Lighter();
			return lighter >= 0 ? lighter : Heavier();
		}
		if (wanted < 400)
		{
			var lighter = Lighter();
			return lighter >= 0 ? lighter : Heavier();
		}
		var heavier = Heavier();
		return heavier >= 0 ? heavier : Lighter();
	}
}
