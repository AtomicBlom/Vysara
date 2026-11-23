namespace Vysara.Text.Atlas;

/// <summary>
/// Maps the handful of Unicode block names a fontset's <c>ranges</c> commonly references to their code-point
/// extents, and parses explicit <c>U+XXXX</c> / <c>U+XXXX-U+YYYY</c> range strings. Not the full block table —
/// just the blocks UI text realistically baked v1.
/// </summary>
internal static class UnicodeRanges
{
	private static readonly Dictionary<string, (int Start, int End)> Blocks = new(StringComparer.OrdinalIgnoreCase)
	{
		["ascii"] = (0x20, 0x7E),
		["printable ascii"] = (0x20, 0x7E),
		["basic latin"] = (0x20, 0x7E),
		["latin-1 supplement"] = (0xA0, 0xFF),
		["latin-1"] = (0xA0, 0xFF),
		["latin extended-a"] = (0x0100, 0x017F),
		["latin extended-b"] = (0x0180, 0x024F),
		["general punctuation"] = (0x2000, 0x206F),
		["currency symbols"] = (0x20A0, 0x20BF),
		["letterlike symbols"] = (0x2100, 0x214F),
		["arrows"] = (0x2190, 0x21FF),
		["mathematical operators"] = (0x2200, 0x22FF),
		["hiragana"] = (0x3040, 0x309F),
		["katakana"] = (0x30A0, 0x30FF),
		["cjk symbols and punctuation"] = (0x3000, 0x303F),
		["cjk unified ideographs"] = (0x4E00, 0x9FFF),
	};

	/// <summary>Resolve one range token (block name or <c>U+</c> range) into code points appended to <paramref name="into"/>.</summary>
	public static void Resolve(string token, HashSet<int> into)
	{
		token = token.Trim();
		if (token.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
		{
			var dash = token.IndexOf('-');
			if (dash < 0)
			{
				if (TryHex(token.AsSpan(2), out var cp))
					into.Add(cp);
				return;
			}

			var loText = token.AsSpan(2, dash - 2);
			var hiText = token[(dash + 1)..].StartsWith("U+", StringComparison.OrdinalIgnoreCase)
				? token.AsSpan(dash + 3)
				: token.AsSpan(dash + 1);
			if (TryHex(loText, out var lo) && TryHex(hiText, out var hi))
				AddRange(into, lo, hi);
			return;
		}

		if (Blocks.TryGetValue(token, out var block))
			AddRange(into, block.Start, block.End);
		else
			throw new InvalidOperationException($"Unknown charset range '{token}' (use a known block name or a U+XXXX[-U+YYYY] range).");
	}

	private static void AddRange(HashSet<int> into, int lo, int hi)
	{
		if (hi < lo)
			(lo, hi) = (hi, lo);
		for (var cp = lo; cp <= hi; cp++)
			into.Add(cp);
	}

	private static bool TryHex(ReadOnlySpan<char> s, out int value) =>
		int.TryParse(s.Trim(), System.Globalization.NumberStyles.HexNumber, null, out value);
}
