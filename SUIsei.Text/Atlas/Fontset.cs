using System.Text.Json;

namespace SUIsei.Text.Atlas;

/// <summary>One font in a fontset: a file path, the ranges it should contribute, and its fallback role/order.</summary>
public sealed record FontEntry(string Path, IReadOnlyList<string> Ranges, bool IsFallback, string? Weight, string? Script)
{
	/// <summary>True when this font is asked to cover <paramref name="codepoint"/> (an empty range list ⇒ covers anything in the global charset).</summary>
	public bool Covers(int codepoint, IReadOnlyCollection<int> resolvedRange) =>
		Ranges.Count == 0 || resolvedRange.Contains(codepoint);
}

/// <summary>
/// A parsed <c>*.fontset.json</c>: the fonts + the charset to bake + the MTSDF parameters. Parsed with
/// <see cref="JsonDocument"/> (no reflection) to stay NativeAOT-clean.
/// </summary>
public sealed class Fontset
{
	public required string Name { get; init; }
	public required IReadOnlyList<FontEntry> Fonts { get; init; }
	public int PxRange { get; init; } = 4;
	public int GlyphSize { get; init; } = 32;
	public int AtlasPageSize { get; init; } = 1024;

	/// <summary>Top-level charset tokens (block names / U+ ranges / "ascii"), in addition to any per-font ranges.</summary>
	public IReadOnlyList<string> Charset { get; init; } = [];

	/// <summary>
	/// The full set of code points to attempt: the union of every font's ranges plus the top-level charset. When no
	/// ranges are given anywhere, defaults to printable ASCII (matching the committed ui-default).
	/// </summary>
	public SortedSet<int> ResolveCharset()
	{
		var set = new HashSet<int>();
		foreach (var token in Charset)
			UnicodeRanges.Resolve(token, set);
		foreach (var font in Fonts)
			foreach (var token in font.Ranges)
				UnicodeRanges.Resolve(token, set);

		if (set.Count == 0)
			UnicodeRanges.Resolve("ascii", set);

		return new SortedSet<int>(set);
	}

	/// <summary>Resolve a single font's own ranges to code points (empty list ⇒ empty set, i.e. "catch-all").</summary>
	public HashSet<int> ResolveFontRange(FontEntry font)
	{
		var set = new HashSet<int>();
		foreach (var token in font.Ranges)
			UnicodeRanges.Resolve(token, set);
		return set;
	}

	public static Fontset Parse(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;

		var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "font" : "font";

		var fonts = new List<FontEntry>();
		if (root.TryGetProperty("fonts", out var fontsEl) && fontsEl.ValueKind == JsonValueKind.Array)
		{
			foreach (var f in fontsEl.EnumerateArray())
			{
				var path = f.GetProperty("path").GetString() ?? throw new InvalidDataException("font entry missing 'path'.");
				var ranges = ReadStringArray(f, "ranges");
				var role = f.TryGetProperty("role", out var r) ? r.GetString() : null;
				var weight = f.TryGetProperty("weight", out var w) ? w.GetString() : null;
				var script = f.TryGetProperty("script", out var s) ? s.GetString() : null;
				fonts.Add(new FontEntry(path, ranges,
					string.Equals(role, "fallback", StringComparison.OrdinalIgnoreCase), weight, script));
			}
		}

		if (fonts.Count == 0)
			throw new InvalidDataException("fontset has no 'fonts'.");

		return new Fontset
		{
			Name = name,
			Fonts = fonts,
			PxRange = root.TryGetProperty("pxRange", out var px) ? px.GetInt32() : 4,
			GlyphSize = root.TryGetProperty("glyphSize", out var gs) ? gs.GetInt32() : 32,
			AtlasPageSize = root.TryGetProperty("atlasPageSize", out var aps) ? aps.GetInt32() : 1024,
			Charset = ReadStringArray(root, "charset"),
		};
	}

	private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string property)
	{
		if (!obj.TryGetProperty(property, out var el))
			return [];
		if (el.ValueKind == JsonValueKind.String)
			return [el.GetString()!];
		if (el.ValueKind != JsonValueKind.Array)
			return [];
		var list = new List<string>();
		foreach (var item in el.EnumerateArray())
			if (item.ValueKind == JsonValueKind.String)
				list.Add(item.GetString()!);
		return list;
	}
}
