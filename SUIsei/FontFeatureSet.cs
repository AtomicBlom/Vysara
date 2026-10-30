namespace SUIsei;

/// <summary>
/// The OpenType feature settings a run of text is shaped with, immutable and compared by value. An
/// <see cref="ITextShaper"/> takes one for both measuring and laying out a run, so the width layout reserves is the
/// width the run draws at. Each tag appears once: a later setting of the same tag replaces an earlier one. The settings
/// are kept in tag order, so two sets that set the same tags to the same values are equal however they were written. Sets are
/// cheap to share; the common ones are kept here so an element switching between them allocates nothing.
/// </summary>
public sealed class FontFeatureSet : IEquatable<FontFeatureSet>
{
	private readonly FontFeature[] _features;

	/// <summary>The settings in <paramref name="features"/>, the last setting of a tag winning.</summary>
	public FontFeatureSet(params ReadOnlySpan<FontFeature> features)
	{
		var list = new List<FontFeature>(features.Length);
		foreach (var feature in features)
		{
			var at = list.FindIndex(f => f.Tag == feature.Tag);
			if (at >= 0)
				list[at] = feature;
			else
				list.Add(feature);
		}

		// In tag order, so equality and the hash don't depend on the order the settings were written in. Each tag is a
		// feature of its own, so the order a shaper takes them in doesn't change what it does.
		list.Sort(static (a, b) => a.Tag.CompareTo(b.Tag));
		_features = [.. list];
	}

	/// <summary>No settings: the font's and the shaper's defaults.</summary>
	public static FontFeatureSet Empty { get; } = new();

	/// <summary><c>tnum</c> on: digits of equal advance.</summary>
	public static FontFeatureSet TabularFigures { get; } = new(FontFeature.TabularFigures);

	/// <summary><c>pnum</c> on: digits of their own widths.</summary>
	public static FontFeatureSet ProportionalFigures { get; } = new(FontFeature.ProportionalFigures);

	/// <summary>The settings, each tag once.</summary>
	public ReadOnlySpan<FontFeature> Features => _features;

	public bool IsEmpty => _features.Length == 0;

	/// <summary>The value this set gives the feature <paramref name="tag"/>, if it sets it.</summary>
	public bool TryGetValue(uint tag, out uint value)
	{
		foreach (var feature in _features)
		{
			if (feature.Tag != tag)
				continue;
			value = feature.Value;
			return true;
		}

		value = 0;
		return false;
	}

	/// <summary>Whether this set turns the feature <paramref name="tag"/> on (sets it to anything but 0).</summary>
	public bool IsOn(uint tag) => TryGetValue(tag, out var value) && value != 0;

	public bool Equals(FontFeatureSet? other) =>
		other is not null && (ReferenceEquals(this, other) || _features.AsSpan().SequenceEqual(other._features));

	public override bool Equals(object? obj) => Equals(obj as FontFeatureSet);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var feature in _features)
			hash.Add(feature);
		return hash.ToHashCode();
	}

	/// <summary>The settings separated by spaces in HarfBuzz's syntax (<c>tnum liga=0</c>), or empty.</summary>
	public override string ToString() => string.Join(' ', _features);
}
