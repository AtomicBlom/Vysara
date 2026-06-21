namespace SUIsei.Text.Atlas;

/// <summary>A glyph's placement in the atlas page (top-left texel, integer).</summary>
internal readonly record struct Placement(int Index, int X, int Y, int Width, int Height);

/// <summary>The packed page: placements (input order) and the trimmed page dimensions.</summary>
internal readonly record struct PackResult(Placement[] Placements, int PageWidth, int PageHeight);

/// <summary>
/// A shelf (row) bin-packer. Glyphs are sorted tallest-first and laid onto horizontal shelves; the page width is
/// fixed (the configured atlas page size) and the height grows as shelves are added, then both dimensions are
/// trimmed to what was actually used. The current single-page <see cref="SUIsei.FontAtlas"/> consumes one page, so
/// everything must fit one page — an overflow is a hard error pointing at a larger page size.
/// </summary>
internal static class ShelfPacker
{
	private const int Gutter = 1;

	public static PackResult Pack(IReadOnlyList<(int Width, int Height)> cells, int pageWidth)
	{
		var order = new int[cells.Count];
		for (var i = 0; i < order.Length; i++)
			order[i] = i;
		Array.Sort(order, (a, b) => cells[b].Height.CompareTo(cells[a].Height));

		var placements = new Placement[cells.Count];
		var shelfX = 0;
		var shelfY = 0;
		var shelfHeight = 0;
		var usedWidth = 0;

		foreach (var idx in order)
		{
			var (w, h) = cells[idx];
			if (w == 0 || h == 0)
			{
				placements[idx] = new Placement(idx, 0, 0, 0, 0);
				continue;
			}

			if (shelfX + w > pageWidth)
			{
				// Start a new shelf.
				shelfY += shelfHeight + Gutter;
				shelfX = 0;
				shelfHeight = 0;
			}

			placements[idx] = new Placement(idx, shelfX, shelfY, w, h);
			shelfX += w + Gutter;
			shelfHeight = Math.Max(shelfHeight, h);
			usedWidth = Math.Max(usedWidth, shelfX - Gutter);
		}

		var usedHeight = shelfY + shelfHeight;
		var pageW = RoundUp(usedWidth, 4);
		var pageH = RoundUp(usedHeight, 4);
		if (pageH > pageWidth || pageW > pageWidth)
			throw new InvalidOperationException(
				$"Glyphs do not fit in a single {pageWidth}×{pageWidth} atlas page (need {pageW}×{pageH}). " +
				"Raise atlasPageSize, lower glyphSize, or reduce the charset.");

		return new PackResult(placements, pageW, pageH);
	}

	private static int RoundUp(int v, int multiple) => (v + multiple - 1) / multiple * multiple;
}
