namespace BinaryVibrance.Vysara;

/// <summary>
/// One OpenType feature setting for a shaper: a four-letter <see cref="Tag"/> such as <c>tnum</c> (packed big-endian, as
/// the font's tables store it) and its <see cref="Value"/>: 1 turns a feature on and 0 off; a feature that picks one of
/// several alternates takes the alternate's index.
/// </summary>
public readonly record struct FontFeature(uint Tag, uint Value)
{
	/// <summary>A setting of the feature named <paramref name="tag"/>, up to four ASCII characters (padded with spaces).</summary>
	public FontFeature(string tag, uint value = 1)
		: this(TagOf(tag), value)
	{
	}

	/// <summary>Tabular figures, <c>tnum</c>: digits of equal advance, so columns of numbers line up.</summary>
	public static FontFeature TabularFigures { get; } = new("tnum");

	/// <summary>Proportional figures, <c>pnum</c>: digits of their own widths.</summary>
	public static FontFeature ProportionalFigures { get; } = new("pnum");

	/// <summary>The tag's four characters.</summary>
	public string TagName => string.Create(4, Tag, static (chars, tag) =>
	{
		for (var i = 0; i < 4; i++)
			chars[i] = (char)((tag >> (24 - 8 * i)) & 0xFF);
	});

	/// <summary>Pack a tag's characters as the font's tables do: up to four printable ASCII characters, padded with spaces.</summary>
	public static uint TagOf(ReadOnlySpan<char> tag)
	{
		if (tag.IsEmpty || tag.Length > 4)
			throw new ArgumentException($"An OpenType tag is one to four characters, not '{tag}'.", nameof(tag));
		var packed = 0u;
		for (var i = 0; i < 4; i++)
		{
			var c = i < tag.Length ? tag[i] : ' ';
			if (c is < ' ' or > '~')
				throw new ArgumentException($"An OpenType tag is printable ASCII, not '{tag}'.", nameof(tag));
			packed = (packed << 8) | c;
		}

		return packed;
	}

	/// <summary><c>tnum</c> for a feature turned on, <c>tnum=0</c> otherwise: HarfBuzz's feature syntax.</summary>
	public override string ToString() => Value == 1 ? TagName.TrimEnd() : $"{TagName.TrimEnd()}={Value}";
}
