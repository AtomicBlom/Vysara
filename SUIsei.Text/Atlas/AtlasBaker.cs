using System.Numerics;
using System.Threading.Tasks;
using SUIsei.Text.Coloring;
using SUIsei.Text.Generation;
using SUIsei.Text.Geometry;
using SUIsei.Text.OpenType;

namespace SUIsei.Text.Atlas;

/// <summary>
/// Orchestrates a fontset into an atlas page + metrics: select a font per code point, generate each glyph's MTSDF
/// cell (glyphs in parallel), shelf-pack the cells, composite + quantize the page, and emit the JSON layout. The
/// only coupling to the engine is the emitted format, produced by <see cref="FontJsonWriter"/>.
/// </summary>
internal static class AtlasBaker
{
	public static BakeResult Bake(Fontset fontset, Func<string, byte[]> resolveFont)
	{
		var fonts = fontset.Fonts.Select(f => new LoadedFont(f, OpenTypeFont.Load(resolveFont(f.Path)), fontset.ResolveFontRange(f))).ToArray();

		// Priority: declared non-fallback fonts first, then fallback fonts.
		var priority = fonts.Where(f => !f.Entry.IsFallback).Concat(fonts.Where(f => f.Entry.IsFallback)).ToArray();
		var primary = priority[0].Font;
		var unitsPerEm = primary.UnitsPerEm;

		var charset = fontset.ResolveCharset();
		var works = new List<GlyphWork>(charset.Count);
		var missing = new List<int>();

		foreach (var cp in charset)
		{
			var chosen = SelectFont(priority, cp);
			if (chosen is null)
			{
				missing.Add(cp);
				continue;
			}

			var glyphId = chosen.Font.GetGlyphIndex(cp);
			var advance = chosen.Font.GetAdvanceWidth(glyphId) / (double)chosen.Font.UnitsPerEm;
			var shape = chosen.Font.GetOutline(glyphId);
			works.Add(new GlyphWork { Codepoint = cp, Advance = advance, Shape = shape.IsEmpty ? null : shape, Font = chosen.Font });
		}

		var geometry = works.Where(w => w.Shape is not null).ToArray();
		Parallel.ForEach(geometry, w => GenerateGlyph(w, fontset.GlyphSize, fontset.PxRange));

		var cells = geometry.Select(w => (w.Width, w.Height)).ToArray();
		var pack = ShelfPacker.Pack(cells, fontset.AtlasPageSize);
		for (var i = 0; i < geometry.Length; i++)
		{
			var p = pack.Placements[i];
			geometry[i].AtlasX = p.X;
			geometry[i].AtlasY = p.Y;
		}

		var page = new byte[pack.PageWidth * pack.PageHeight * 4];
		Parallel.ForEach(geometry, w => Composite(w, page, pack.PageWidth));

		var json = FontJsonWriter.Write(fontset, pack.PageWidth, pack.PageHeight, unitsPerEm,
			primary.TypoAscender, primary.TypoDescender, primary.TypoLineGap, works);
		var png = PngWriter.Encode(pack.PageWidth, pack.PageHeight, page);

		return new BakeResult
		{
			Name = fontset.Name,
			PngPage = png,
			FontJson = json,
			Width = pack.PageWidth,
			Height = pack.PageHeight,
			MissingCodepoints = missing,
		};
	}

	private static LoadedFont? SelectFont(LoadedFont[] priority, int cp)
	{
		foreach (var lf in priority)
			if (lf.Entry.Covers(cp, lf.Range) && lf.Font.HasGlyph(cp))
				return lf;
		return null;
	}

	private static void GenerateGlyph(GlyphWork w, int glyphSize, int pxRange)
	{
		var shape = w.Shape!;
		var unitsPerEm = w.Font!.UnitsPerEm;
		var scale = (double)glyphSize / unitsPerEm;        // texels per font unit
		var unitsPerTexel = 1.0 / scale;                   // font units per texel
		var rangeUnits = pxRange * unitsPerTexel;          // the distance range in font units
		var half = rangeUnits / 2;

		var ink = shape.ComputeBounds();
		var boxLeft = ink.Left - half;
		var boxBottom = ink.Bottom - half;
		var boxRight = ink.Right + half;
		var boxTop = ink.Top + half;

		// Round the cell to whole texels, expanding the box symmetrically so plane↔atlas stay 1:1 at scale.
		var pw = Math.Max(1, (int)Math.Ceiling((boxRight - boxLeft) * scale));
		var ph = Math.Max(1, (int)Math.Ceiling((boxTop - boxBottom) * scale));
		var targetW = pw * unitsPerTexel;
		var targetH = ph * unitsPerTexel;
		var ex = (targetW - (boxRight - boxLeft)) / 2;
		var ey = (targetH - (boxTop - boxBottom)) / 2;
		boxLeft -= ex; boxRight = boxLeft + targetW;
		boxBottom -= ey; boxTop = boxBottom + targetH;

		EdgeColoring.ColorSimple(shape);
		var projection = new CellProjection(boxLeft, boxTop, unitsPerTexel);
		w.Bitmap = MsdfGenerator.Generate(shape, pw, ph, rangeUnits, pxRange, projection, parallelRows: false);
		w.Width = pw;
		w.Height = ph;

		// Plane bounds in EM (y-down, baseline origin): box·scale/glyphSize = box/unitsPerEm.
		w.PlaneLeft = boxLeft / unitsPerEm;
		w.PlaneRight = boxRight / unitsPerEm;
		w.PlaneTop = -boxTop / unitsPerEm;
		w.PlaneBottom = -boxBottom / unitsPerEm;
	}

	private static void Composite(GlyphWork w, byte[] page, int pageWidth)
	{
		var bmp = w.Bitmap!;
		var dst = new byte[bmp.Width * 4];
		for (var py = 0; py < bmp.Height; py++)
		{
			var src = bmp.Pixels.AsSpan(py * bmp.Width * 4, bmp.Width * 4);
			QuantizeRow(src, dst);
			var pageOffset = ((w.AtlasY + py) * pageWidth + w.AtlasX) * 4;
			dst.AsSpan().CopyTo(page.AsSpan(pageOffset));
		}
	}

	/// <summary>Clamp + scale linear 0..1 floats to bytes; the arithmetic is vectorized, the byte stores are scalar.</summary>
	private static void QuantizeRow(ReadOnlySpan<float> src, Span<byte> dst)
	{
		var i = 0;
		var width = Vector<float>.Count;
		var s255 = new Vector<float>(255f);
		var half = new Vector<float>(0.5f);
		var lo = Vector<float>.Zero;
		var hi = s255;
		for (; i + width <= src.Length; i += width)
		{
			var v = new Vector<float>(src.Slice(i, width));
			v = Vector.Max(lo, Vector.Min(hi, v * s255 + half));
			var ints = Vector.ConvertToInt32(v);
			for (var k = 0; k < width; k++)
				dst[i + k] = (byte)ints[k];
		}

		for (; i < src.Length; i++)
			dst[i] = (byte)Math.Clamp((int)(src[i] * 255f + 0.5f), 0, 255);
	}

	private sealed record LoadedFont(FontEntry Entry, OpenTypeFont Font, HashSet<int> Range);
}
