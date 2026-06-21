using SUIsei.Text.Geometry;

namespace SUIsei.Text.OpenType;

/// <summary>
/// A hand-rolled OpenType/TrueType font reader — no external font dependency. Parses the sfnt table directory and
/// the tables the MTSDF baker needs (<c>head</c>, <c>maxp</c>, <c>hhea</c>/<c>hmtx</c>, <c>OS/2</c>, <c>cmap</c>,
/// and the outline tables) and exposes code-point → glyph lookup, advances, EM metrics, and glyph outlines. Outline
/// extraction is delegated to an <see cref="IGlyphOutlineSource"/> (TrueType <c>glyf</c>; OpenType <c>CFF</c>).
/// </summary>
public sealed class OpenTypeFont
{
	private readonly IGlyphOutlineSource _outlines;
	private readonly CmapSubtable? _cmap;
	private readonly ushort[] _advances; // per longHorMetric, last entry repeats for trailing glyphs

	private OpenTypeFont(int unitsPerEm, int numGlyphs, int typoAscender, int typoDescender, int typoLineGap,
		IGlyphOutlineSource outlines, CmapSubtable? cmap, ushort[] advances)
	{
		UnitsPerEm = unitsPerEm;
		NumGlyphs = numGlyphs;
		TypoAscender = typoAscender;
		TypoDescender = typoDescender;
		TypoLineGap = typoLineGap;
		_outlines = outlines;
		_cmap = cmap;
		_advances = advances;
	}

	/// <summary>Font design units per EM (the outline coordinate scale; commonly 1000 or 2048).</summary>
	public int UnitsPerEm { get; }

	public int NumGlyphs { get; }

	/// <summary>OS/2 typographic ascender/descender/line-gap in font units (the values UI line metrics derive from).</summary>
	public int TypoAscender { get; }
	public int TypoDescender { get; }
	public int TypoLineGap { get; }

	/// <summary>Glyph id for a Unicode code point, or 0 (.notdef) when the font does not cover it.</summary>
	public int GetGlyphIndex(int codepoint) => _cmap?.GetGlyph(codepoint) ?? 0;

	/// <summary>True when the font has a non-.notdef glyph for the code point.</summary>
	public bool HasGlyph(int codepoint) => GetGlyphIndex(codepoint) != 0;

	/// <summary>Horizontal advance of a glyph in font units.</summary>
	public int GetAdvanceWidth(int glyphId)
	{
		if (_advances.Length == 0)
			return 0;
		return glyphId < _advances.Length ? _advances[glyphId] : _advances[^1];
	}

	/// <summary>The glyph outline as contours in font units, y-up. Empty for blank glyphs.</summary>
	internal Shape GetOutline(int glyphId) => _outlines.GetOutline(glyphId);

	/// <summary>Parse a font from its raw sfnt bytes (a single .ttf/.otf; font collections are not supported).</summary>
	public static OpenTypeFont Load(byte[] data)
	{
		var file = new SfntReader(data);
		var sfntVersion = file.ReadUInt32();
		if (sfntVersion == 0x74746366) // 'ttcf'
			throw new NotSupportedException("Font collections (.ttc) are not supported; extract a single face.");

		int numTables = file.ReadUInt16();
		file.Skip(6); // searchRange, entrySelector, rangeShift

		var tables = new Dictionary<string, (int Offset, int Length)>(numTables);
		for (var i = 0; i < numTables; i++)
		{
			var tag = file.ReadTag();
			file.ReadUInt32(); // checksum
			var offset = (int)file.ReadUInt32();
			var length = (int)file.ReadUInt32();
			tables[tag] = (offset, length);
		}

		if (!tables.TryGetValue("head", out var head))
			throw new InvalidDataException("Missing 'head' table.");
		var hr = file.At(head.Offset);
		hr.Skip(18); // version, fontRevision, checkSumAdjustment, magicNumber, flags
		int unitsPerEm = hr.ReadUInt16();
		hr.Skip(30); // created, modified, xMin..yMax, macStyle, lowestRecPPEM, fontDirectionHint
		int indexToLocFormat = hr.ReadInt16();

		if (!tables.TryGetValue("maxp", out var maxp))
			throw new InvalidDataException("Missing 'maxp' table.");
		var mr = file.At(maxp.Offset);
		mr.ReadUInt32(); // version
		int numGlyphs = mr.ReadUInt16();

		var (typoAsc, typoDesc, typoGap) = ReadMetrics(file, tables, unitsPerEm);
		var advances = ReadAdvances(file, tables, numGlyphs);
		var cmap = tables.TryGetValue("cmap", out var c) ? CmapSubtable.Parse(file, c.Offset) : null;
		var outlines = CreateOutlineSource(file, tables, indexToLocFormat, numGlyphs);

		return new OpenTypeFont(unitsPerEm, numGlyphs, typoAsc, typoDesc, typoGap, outlines, cmap, advances);
	}

	private static (int Asc, int Desc, int Gap) ReadMetrics(
		SfntReader file, Dictionary<string, (int Offset, int Length)> tables, int unitsPerEm)
	{
		// Prefer OS/2 typo metrics (what UI line layout wants); fall back to hhea.
		if (tables.TryGetValue("OS/2", out var os2))
		{
			var r = file.At(os2.Offset + 68); // sTypoAscender
			int asc = r.ReadInt16();
			int desc = r.ReadInt16();
			int gap = r.ReadInt16();
			return (asc, desc, gap);
		}

		if (tables.TryGetValue("hhea", out var hhea))
		{
			var r = file.At(hhea.Offset + 4); // ascender
			int asc = r.ReadInt16();
			int desc = r.ReadInt16();
			int gap = r.ReadInt16();
			return (asc, desc, gap);
		}

		// Last resort: a plausible 80/20 split of the EM.
		return ((int)(0.8 * unitsPerEm), -(int)(0.2 * unitsPerEm), 0);
	}

	private static ushort[] ReadAdvances(
		SfntReader file, Dictionary<string, (int Offset, int Length)> tables, int numGlyphs)
	{
		if (!tables.TryGetValue("hhea", out var hhea) || !tables.TryGetValue("hmtx", out var hmtx))
			return [];

		var hr = file.At(hhea.Offset + 34); // numberOfHMetrics
		int numberOfHMetrics = hr.ReadUInt16();
		if (numberOfHMetrics <= 0)
			return [];

		var advances = new ushort[numberOfHMetrics];
		var mr = file.At(hmtx.Offset);
		for (var i = 0; i < numberOfHMetrics; i++)
		{
			advances[i] = mr.ReadUInt16();
			mr.ReadInt16(); // leftSideBearing
		}

		return advances;
	}

	private static IGlyphOutlineSource CreateOutlineSource(
		SfntReader file, Dictionary<string, (int Offset, int Length)> tables, int indexToLocFormat, int numGlyphs)
	{
		if (tables.TryGetValue("glyf", out var glyf) && tables.TryGetValue("loca", out var loca))
		{
			var locaOffsets = ReadLoca(file, loca.Offset, indexToLocFormat, numGlyphs);
			return new GlyfOutlineSource(file, glyf.Offset, locaOffsets);
		}

		if (tables.TryGetValue("CFF ", out var cff))
			return new CffOutlineSource(file, cff.Offset, numGlyphs);

		throw new NotSupportedException("Font has neither a 'glyf' nor a 'CFF ' outline table.");
	}

	private static uint[] ReadLoca(SfntReader file, int locaOffset, int indexToLocFormat, int numGlyphs)
	{
		var count = numGlyphs + 1;
		var offsets = new uint[count];
		var r = file.At(locaOffset);
		if (indexToLocFormat == 0) // short: u16 offsets, doubled
		{
			for (var i = 0; i < count; i++)
				offsets[i] = (uint)r.ReadUInt16() * 2;
		}
		else // long: u32 offsets
		{
			for (var i = 0; i < count; i++)
				offsets[i] = r.ReadUInt32();
		}

		return offsets;
	}
}
