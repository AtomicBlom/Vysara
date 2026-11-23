using Vysara.Text.Geometry;

namespace Vysara.Text.OpenType;

/// <summary>
/// TrueType <c>glyf</c> outline source: reads quadratic-Bézier glyph outlines via the <c>loca</c> offset table,
/// resolving composite glyphs (component references + 2×2 transform + offset) recursively.
/// </summary>
internal sealed class GlyfOutlineSource(SfntReader file, int glyfOffset, uint[] locaOffsets) : IGlyphOutlineSource
{
	private const int CompositeDepthLimit = 8;
	private readonly int _numGlyphs = locaOffsets.Length - 1;

	public Shape GetOutline(int glyphId)
	{
		var shape = new Shape();
		foreach (var pts in ReadContours(glyphId, 0))
		{
			if (pts.Count > 0)
				shape.Contours.Add(ContourBuilder.Build(pts));
		}

		return shape;
	}

	private List<List<GlyphContourPoint>> ReadContours(int glyphId, int depth)
	{
		var result = new List<List<GlyphContourPoint>>();
		if (glyphId < 0 || glyphId >= _numGlyphs || depth > CompositeDepthLimit)
			return result;

		var start = locaOffsets[glyphId];
		var end = locaOffsets[glyphId + 1];
		if (end <= start)
			return result; // empty glyph (e.g. space)

		var r = file.At(glyfOffset + start);
		int numberOfContours = r.ReadInt16();
		r.Skip(8); // xMin, yMin, xMax, yMax

		if (numberOfContours >= 0)
			ReadSimpleGlyph(r, numberOfContours, result);
		else
			ReadCompositeGlyph(r, depth, result);

		return result;
	}

	private static void ReadSimpleGlyph(SfntReader r, int numberOfContours, List<List<GlyphContourPoint>> result)
	{
		if (numberOfContours == 0)
			return;

		var endPts = new int[numberOfContours];
		for (var i = 0; i < numberOfContours; i++)
			endPts[i] = r.ReadUInt16();
		var numPoints = endPts[^1] + 1;
		if (numPoints <= 0)
			return;

		int instructionLength = r.ReadUInt16();
		r.Skip(instructionLength);

		var flags = new byte[numPoints];
		for (var i = 0; i < numPoints;)
		{
			var f = r.ReadByte();
			flags[i++] = f;
			if ((f & 0x08) != 0) // REPEAT_FLAG
			{
				int repeat = r.ReadByte();
				while (repeat-- > 0 && i < numPoints)
					flags[i++] = f;
			}
		}

		var xs = new int[numPoints];
		var x = 0;
		for (var i = 0; i < numPoints; i++)
		{
			var f = flags[i];
			if ((f & 0x02) != 0) // X_SHORT
				x += (f & 0x10) != 0 ? r.ReadByte() : -r.ReadByte();
			else if ((f & 0x10) == 0) // not "same as previous"
				x += r.ReadInt16();
			xs[i] = x;
		}

		var ys = new int[numPoints];
		var y = 0;
		for (var i = 0; i < numPoints; i++)
		{
			var f = flags[i];
			if ((f & 0x04) != 0) // Y_SHORT
				y += (f & 0x20) != 0 ? r.ReadByte() : -r.ReadByte();
			else if ((f & 0x20) == 0)
				y += r.ReadInt16();
			ys[i] = y;
		}

		var pointStart = 0;
		foreach (var endPt in endPts)
		{
			var contour = new List<GlyphContourPoint>(endPt - pointStart + 1);
			for (var i = pointStart; i <= endPt; i++)
				contour.Add(new GlyphContourPoint(new Vector2D(xs[i], ys[i]), (flags[i] & 0x01) != 0));
			result.Add(contour);
			pointStart = endPt + 1;
		}
	}

	private void ReadCompositeGlyph(SfntReader r, int depth, List<List<GlyphContourPoint>> result)
	{
		while (true)
		{
			int flags = r.ReadUInt16();
			int componentGlyph = r.ReadUInt16();

			double arg1, arg2;
			if ((flags & 0x0001) != 0) // ARG_1_AND_2_ARE_WORDS
			{
				arg1 = r.ReadInt16();
				arg2 = r.ReadInt16();
			}
			else
			{
				arg1 = r.ReadSByte();
				arg2 = r.ReadSByte();
			}

			double a = 1, b = 0, c = 0, d = 1;
			if ((flags & 0x0008) != 0) // WE_HAVE_A_SCALE
			{
				a = d = F2Dot14(r.ReadInt16());
			}
			else if ((flags & 0x0040) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
			{
				a = F2Dot14(r.ReadInt16());
				d = F2Dot14(r.ReadInt16());
			}
			else if ((flags & 0x0080) != 0) // WE_HAVE_A_TWO_BY_TWO
			{
				a = F2Dot14(r.ReadInt16());
				b = F2Dot14(r.ReadInt16());
				c = F2Dot14(r.ReadInt16());
				d = F2Dot14(r.ReadInt16());
			}

			// Point-matching offsets (ARGS_ARE_XY_VALUES clear) are unsupported; treat as no offset.
			var dx = (flags & 0x0002) != 0 ? arg1 : 0;
			var dy = (flags & 0x0002) != 0 ? arg2 : 0;

			foreach (var pc in ReadContours(componentGlyph, depth + 1))
			{
				var transformed = new List<GlyphContourPoint>(pc.Count);
				foreach (var p in pc)
				{
					var tx = a * p.Position.X + c * p.Position.Y + dx;
					var ty = b * p.Position.X + d * p.Position.Y + dy;
					transformed.Add(new GlyphContourPoint(new Vector2D(tx, ty), p.OnCurve));
				}

				result.Add(transformed);
			}

			if ((flags & 0x0020) == 0) // no MORE_COMPONENTS
				break;
		}
	}

	private static double F2Dot14(short v) => v / 16384.0;
}
