using System.Text.Json;

namespace BinaryVibrance.Vysara.Text.Atlas;

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
	/// <summary>
	/// The MTSDF distance range, in atlas texels, of a bake that names none. At <see cref="DefaultGlyphSize"/> the field
	/// reaches ±6 texels, ±0.1875 EM, past each glyph's edge: 2.6 px at 14 px text, room for a visible outline or a soft
	/// shadow drawn from the true-distance channel.
	/// </summary>
	public const int DefaultPxRange = 12;

	/// <summary>The EM size, in atlas texels, of a bake that names none.</summary>
	public const int DefaultGlyphSize = 32;

	/// <summary>The largest atlas page dimension, in texels, of a bake that names none.</summary>
	public const int DefaultAtlasPageSize = 1024;
	public required string Name { get; init; }
	public required IReadOnlyList<FontEntry> Fonts { get; init; }
	public int PxRange { get; init; } = DefaultPxRange;
	public int GlyphSize { get; init; } = DefaultGlyphSize;
	public int AtlasPageSize { get; init; } = DefaultAtlasPageSize;

	/// <summary>When true, the atlas is keyed by font glyph id (each entry carries <c>glyphId</c>, plus <c>unicode</c>
	/// where one exists) so a shaper can address it; when false (the default) it is keyed by code point. Opt-in via the
	/// fontset's <c>"keying": "glyphId"</c>. The tool and the committed <c>ui-default</c> stay code-point-keyed.</summary>
	public bool GlyphIdKeyed { get; init; }

	/// <summary>Top-level charset tokens (block names / U+ ranges / "ascii"), in addition to any per-font ranges.</summary>
	public IReadOnlyList<string> Charset { get; init; } = [];

	/// <summary>
	/// The full set of code points to attempt: the union of every font's ranges plus the top-level charset. When no
	/// ranges are given anywhere, defaults to printable ASCII.
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
			PxRange = root.TryGetProperty("pxRange", out var px) ? px.GetInt32() : DefaultPxRange,
			GlyphSize = root.TryGetProperty("glyphSize", out var gs) ? gs.GetInt32() : DefaultGlyphSize,
			AtlasPageSize = root.TryGetProperty("atlasPageSize", out var aps) ? aps.GetInt32() : DefaultAtlasPageSize,
			Charset = ReadStringArray(root, "charset"),
			GlyphIdKeyed = root.TryGetProperty("keying", out var k)
				&& string.Equals(k.GetString(), "glyphId", StringComparison.OrdinalIgnoreCase),
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
