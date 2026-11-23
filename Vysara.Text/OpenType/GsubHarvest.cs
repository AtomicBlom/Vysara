namespace Vysara.Text.OpenType;

/// <summary>
/// Harvests every glyph id a font's <c>GSUB</c> table can <em>produce</em> as a substitution output — not a full
/// shaping engine, just the set of reachable output glyphs. A glyph-id-keyed atlas unions these with the cmap
/// glyphs so that ligature/contextual glyphs (which have no code point, e.g. Cascadia's programming-ligature forms)
/// are baked and a shaper can resolve them. Walks the LookupList collecting outputs from Single (1), Multiple (2),
/// Alternate (3), Ligature (4), Reverse-chaining (8), and unwrapped Extension (7) subtables; Contextual / Chaining
/// (5/6) carry no outputs of their own — they invoke other lookups, which are harvested directly.
/// </summary>
internal static class GsubHarvest
{
	public static void CollectOutputGlyphs(SfntReader file, int gsubOffset, HashSet<int> into)
	{
		var r = file.At(gsubOffset);
		r.Skip(8); // majorVersion, minorVersion, scriptListOffset, featureListOffset (u16 each)
		int lookupListOffset = r.ReadUInt16();
		if (lookupListOffset == 0)
			return;

		var lookupListStart = gsubOffset + lookupListOffset;
		var lr = file.At(lookupListStart);
		int lookupCount = lr.ReadUInt16();
		var lookupOffsets = new int[lookupCount];
		for (var i = 0; i < lookupCount; i++)
			lookupOffsets[i] = lookupListStart + lr.ReadUInt16();

		foreach (var lookupOffset in lookupOffsets)
		{
			var lkr = file.At(lookupOffset);
			int lookupType = lkr.ReadUInt16();
			lkr.ReadUInt16(); // lookupFlag
			int subTableCount = lkr.ReadUInt16();
			for (var i = 0; i < subTableCount; i++)
				ParseSubtable(file, lookupOffset + lkr.ReadUInt16(), lookupType, into);
		}
	}

	private static void ParseSubtable(SfntReader file, int offset, int lookupType, HashSet<int> into)
	{
		var r = file.At(offset);
		switch (lookupType)
		{
			case 1: // Single substitution
				{
					int format = r.ReadUInt16();
					int coverageOffset = offset + r.ReadUInt16();
					if (format == 1)
					{
						int delta = r.ReadInt16();
						var covered = new List<int>();
						ReadCoverage(file, coverageOffset, covered);
						foreach (var g in covered)
							into.Add((g + delta) & 0xFFFF); // per spec the sum wraps within the 16-bit glyph space
					}
					else // format 2: explicit substitute list
					{
						int count = r.ReadUInt16();
						for (var i = 0; i < count; i++)
							into.Add(r.ReadUInt16());
					}
					break;
				}
			case 2: // Multiple substitution: one glyph -> a sequence
				CollectFromSetsOfGlyphLists(file, r, offset, into);
				break;
			case 3: // Alternate substitution: one glyph -> a set of alternates
				CollectFromSetsOfGlyphLists(file, r, offset, into);
				break;
			case 4: // Ligature substitution: a sequence -> one ligature glyph
				{
					r.ReadUInt16(); // substFormat
					r.ReadUInt16(); // coverageOffset (unused — outputs are explicit)
					int ligSetCount = r.ReadUInt16();
					var ligSetOffsets = new int[ligSetCount];
					for (var i = 0; i < ligSetCount; i++)
						ligSetOffsets[i] = offset + r.ReadUInt16();
					foreach (var ligSetOffset in ligSetOffsets)
					{
						var lsr = file.At(ligSetOffset);
						int ligCount = lsr.ReadUInt16();
						var ligOffsets = new int[ligCount];
						for (var i = 0; i < ligCount; i++)
							ligOffsets[i] = ligSetOffset + lsr.ReadUInt16();
						foreach (var ligOffset in ligOffsets)
							into.Add(file.At(ligOffset).ReadUInt16()); // ligatureGlyph is the first field
					}
					break;
				}
			case 7: // Extension: a wrapper pointing at a real subtable of extensionLookupType
				{
					r.ReadUInt16(); // substFormat (1)
					int extensionLookupType = r.ReadUInt16();
					long extensionOffset = r.ReadUInt32();
					ParseSubtable(file, (int)(offset + extensionOffset), extensionLookupType, into);
					break;
				}
			case 8: // Reverse chaining single substitution
				{
					r.ReadUInt16(); // substFormat
					r.ReadUInt16(); // coverageOffset
					int backtrackCount = r.ReadUInt16();
					r.Skip(backtrackCount * 2);
					int lookaheadCount = r.ReadUInt16();
					r.Skip(lookaheadCount * 2);
					int glyphCount = r.ReadUInt16();
					for (var i = 0; i < glyphCount; i++)
						into.Add(r.ReadUInt16());
					break;
				}
				// 5 (Contextual) / 6 (Chaining contextual): no direct outputs — they sequence calls to other lookups,
				// which are in the LookupList and harvested on their own.
		}
	}

	// Multiple (2) and Alternate (3) share a layout: format, coverage, count, then `count` offsets each to a
	// (glyphCount, glyphIDs[]) list whose glyphs are all outputs.
	private static void CollectFromSetsOfGlyphLists(SfntReader file, SfntReader r, int offset, HashSet<int> into)
	{
		r.ReadUInt16(); // substFormat
		r.ReadUInt16(); // coverageOffset
		int setCount = r.ReadUInt16();
		var setOffsets = new int[setCount];
		for (var i = 0; i < setCount; i++)
			setOffsets[i] = offset + r.ReadUInt16();
		foreach (var setOffset in setOffsets)
		{
			var sr = file.At(setOffset);
			int glyphCount = sr.ReadUInt16();
			for (var i = 0; i < glyphCount; i++)
				into.Add(sr.ReadUInt16());
		}
	}

	private static void ReadCoverage(SfntReader file, int coverageOffset, List<int> glyphs)
	{
		var r = file.At(coverageOffset);
		int format = r.ReadUInt16();
		if (format == 1)
		{
			int count = r.ReadUInt16();
			for (var i = 0; i < count; i++)
				glyphs.Add(r.ReadUInt16());
		}
		else if (format == 2)
		{
			int rangeCount = r.ReadUInt16();
			for (var i = 0; i < rangeCount; i++)
			{
				int start = r.ReadUInt16();
				int end = r.ReadUInt16();
				r.ReadUInt16(); // startCoverageIndex
				for (var g = start; g <= end; g++)
					glyphs.Add(g);
			}
		}
	}
}
