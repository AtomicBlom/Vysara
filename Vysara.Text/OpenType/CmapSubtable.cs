namespace Vysara.Text.OpenType;

/// <summary>
/// A parsed character-to-glyph map: cmap format 4 (BMP segment mapping) and format 12 (full UCS-4 segmented
/// coverage) — the two that cover essentially all modern fonts. Lookups are on demand so a huge format-12 coverage
/// never materializes a dictionary.
/// </summary>
internal sealed class CmapSubtable
{
	// Format 4
	private readonly ushort[] _endCode = [];
	private readonly ushort[] _startCode = [];
	private readonly short[] _idDelta = [];
	private readonly ushort[] _idRangeOffset = [];
	private readonly ushort[] _glyphIdArray = [];
	private readonly int _segCount;

	// Format 12
	private readonly uint[] _grpStart = [];
	private readonly uint[] _grpEnd = [];
	private readonly uint[] _grpGlyph = [];

	private readonly int _format;

	private CmapSubtable(int format, ushort[] endCode, ushort[] startCode, short[] idDelta, ushort[] idRangeOffset,
		ushort[] glyphIdArray, uint[] grpStart, uint[] grpEnd, uint[] grpGlyph)
	{
		_format = format;
		_endCode = endCode;
		_startCode = startCode;
		_idDelta = idDelta;
		_idRangeOffset = idRangeOffset;
		_glyphIdArray = glyphIdArray;
		_segCount = endCode.Length;
		_grpStart = grpStart;
		_grpEnd = grpEnd;
		_grpGlyph = grpGlyph;
	}

	/// <summary>Glyph id for a Unicode code point, or 0 (.notdef) when unmapped.</summary>
	public int GetGlyph(int codepoint)
	{
		return _format switch
		{
			4 => Format4(codepoint),
			12 => Format12(codepoint),
			_ => 0,
		};
	}

	private int Format4(int c)
	{
		if (c > 0xFFFF)
			return 0;
		for (var i = 0; i < _segCount; i++)
		{
			if (_endCode[i] < c)
				continue;
			if (_startCode[i] > c)
				return 0;
			if (_idRangeOffset[i] == 0)
				return (c + _idDelta[i]) & 0xFFFF;

			// idRangeOffset indexes into glyphIdArray, measured in u16 from the idRangeOffset[i] slot itself.
			var index = _idRangeOffset[i] / 2 + (c - _startCode[i]) - (_segCount - i);
			if (index < 0 || index >= _glyphIdArray.Length)
				return 0;
			var g = _glyphIdArray[index];
			return g == 0 ? 0 : (g + _idDelta[i]) & 0xFFFF;
		}

		return 0;
	}

	private int Format12(int c)
	{
		var uc = (uint)c;
		for (var i = 0; i < _grpStart.Length; i++)
			if (uc >= _grpStart[i] && uc <= _grpEnd[i])
				return (int)(_grpGlyph[i] + (uc - _grpStart[i]));
		return 0;
	}

	/// <summary>Pick the best subtable from the cmap table and parse it. Returns null if no supported format is found.</summary>
	public static CmapSubtable? Parse(SfntReader file, int cmapOffset)
	{
		var r = file.At(cmapOffset);
		r.ReadUInt16(); // version
		int numTables = r.ReadUInt16();

		var bestOffset = -1;
		var bestScore = -1;
		for (var i = 0; i < numTables; i++)
		{
			int platform = r.ReadUInt16();
			int encoding = r.ReadUInt16();
			var subOffset = (int)r.ReadUInt32();
			var score = ScoreEncoding(platform, encoding);
			if (score > bestScore)
			{
				bestScore = score;
				bestOffset = cmapOffset + subOffset;
			}
		}

		if (bestOffset < 0)
			return null;

		var sub = file.At(bestOffset);
		int format = sub.ReadUInt16();
		return format switch
		{
			4 => ParseFormat4(sub, bestOffset),
			12 => ParseFormat12(sub),
			_ => null,
		};
	}

	private static int ScoreEncoding(int platform, int encoding) => (platform, encoding) switch
	{
		(3, 10) => 5, // Windows UCS-4
		(0, 6) => 5,  // Unicode full
		(0, 4) => 5,
		(3, 1) => 4,  // Windows BMP
		(0, 3) => 4,
		(0, _) => 3,
		(3, 0) => 1,  // Windows symbol
		_ => 0,
	};

	private static CmapSubtable ParseFormat4(SfntReader r, int subtableOffset)
	{
		r.ReadUInt16(); // length
		r.ReadUInt16(); // language
		int segX2 = r.ReadUInt16();
		var segCount = segX2 / 2;
		r.ReadUInt16(); // searchRange
		r.ReadUInt16(); // entrySelector
		r.ReadUInt16(); // rangeShift

		var endCode = new ushort[segCount];
		for (var i = 0; i < segCount; i++) endCode[i] = r.ReadUInt16();
		r.ReadUInt16(); // reservedPad
		var startCode = new ushort[segCount];
		for (var i = 0; i < segCount; i++) startCode[i] = r.ReadUInt16();
		var idDelta = new short[segCount];
		for (var i = 0; i < segCount; i++) idDelta[i] = r.ReadInt16();

		// idRangeOffset values are relative to their own slot; the glyphIdArray follows them.
		var idRangeOffset = new ushort[segCount];
		for (var i = 0; i < segCount; i++) idRangeOffset[i] = r.ReadUInt16();

		// Remaining bytes of the subtable are the glyphIdArray.
		var glyphArrayStart = r.Position;
		var subtableLength = subtableOffset + ReadLengthAt(r, subtableOffset);
		var remaining = Math.Max(0, (subtableLength - glyphArrayStart) / 2);
		var glyphIdArray = new ushort[remaining];
		var gr = r.At(glyphArrayStart);
		for (var i = 0; i < remaining; i++) glyphIdArray[i] = gr.ReadUInt16();

		return new CmapSubtable(4, endCode, startCode, idDelta, idRangeOffset, glyphIdArray, [], [], []);
	}

	private static ushort ReadLengthAt(SfntReader r, int subtableOffset)
	{
		// length is the u16 right after the format word at the subtable start.
		var lr = r.At(subtableOffset + 2);
		return lr.ReadUInt16();
	}

	private static CmapSubtable ParseFormat12(SfntReader r)
	{
		r.ReadUInt16(); // reserved
		r.ReadUInt32(); // length
		r.ReadUInt32(); // language
		var numGroups = (int)r.ReadUInt32();
		var start = new uint[numGroups];
		var end = new uint[numGroups];
		var glyph = new uint[numGroups];
		for (var i = 0; i < numGroups; i++)
		{
			start[i] = r.ReadUInt32();
			end[i] = r.ReadUInt32();
			glyph[i] = r.ReadUInt32();
		}

		return new CmapSubtable(12, [], [], [], [], [], start, end, glyph);
	}
}
