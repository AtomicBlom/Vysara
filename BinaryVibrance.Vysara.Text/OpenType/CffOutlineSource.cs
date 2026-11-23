using BinaryVibrance.Vysara.Text.Geometry;

namespace BinaryVibrance.Vysara.Text.OpenType;

/// <summary>
/// OpenType <c>CFF</c> (Compact Font Format) outline source: a Type2 charstring interpreter that turns a glyph's
/// charstring into cubic-Bézier contours. Supports plain and CID-keyed CFF (FDArray/FDSelect for per-glyph local
/// subroutines). Hinting operators are parsed for stack/width bookkeeping but otherwise ignored.
/// </summary>
internal sealed class CffOutlineSource : IGlyphOutlineSource
{
	private const int SubrDepthLimit = 10;

	private readonly CffIndex _charStrings;
	private readonly CffIndex _globalSubrs;
	private readonly int _globalBias;

	// Non-CID: a single local-subr set. CID: one per font-dict, chosen via _fdSelect.
	private readonly CffIndex?[] _localSubrs;
	private readonly int[] _localBias;
	private readonly byte[]? _fdSelect; // gid -> fd index (CID only)

	public CffOutlineSource(SfntReader file, int cffOffset, int numGlyphs)
	{
		var data = file.Raw;
		var r = file.At(cffOffset);
		int hdrSize = data[cffOffset + 2];
		r.Position = cffOffset + hdrSize;

		CffIndex.Read(data, r); // Name INDEX
		var topDicts = CffIndex.Read(data, r);
		CffIndex.Read(data, r); // String INDEX
		_globalSubrs = CffIndex.Read(data, r);
		_globalBias = Bias(_globalSubrs.Count);

		var topDict = CffDict.Parse(topDicts.Get(0));

		var charStringsOffset = (int)topDict.GetSingle(17, 0);
		_charStrings = CffIndex.Read(data, file.At(cffOffset + charStringsOffset));

		var isCid = topDict.TryGet(1230, out _); // ROS operator ⇒ CID-keyed

		if (isCid)
		{
			(_localSubrs, _localBias, _fdSelect) = ReadCidSubrs(data, file, cffOffset, topDict, numGlyphs);
		}
		else
		{
			var (local, bias) = ReadPrivateLocalSubrs(data, file, cffOffset, topDict);
			_localSubrs = [local];
			_localBias = [bias];
			_fdSelect = null;
		}
	}

	public Shape GetOutline(int glyphId)
	{
		var shape = new Shape();
		if (glyphId < 0 || glyphId >= _charStrings.Count)
			return shape;

		var fd = _fdSelect is not null && glyphId < _fdSelect.Length ? _fdSelect[glyphId] : 0;
		var interp = new Type2Interpreter(_globalSubrs, _globalBias, _localSubrs[fd], _localBias[fd]);
		interp.Run(_charStrings.Get(glyphId), shape);
		return shape;
	}

	private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

	private static (CffIndex? Local, int Bias) ReadPrivateLocalSubrs(
		byte[] data, SfntReader file, int cffOffset, CffDict topDict)
	{
		if (!topDict.TryGet(18, out var priv) || priv.Length < 2)
			return (null, 0);

		var privSize = (int)priv[0];
		var privOffset = cffOffset + (int)priv[1];
		var privDict = CffDict.Parse(data.AsSpan(privOffset, privSize));
		var subrsRel = (int)privDict.GetSingle(19, 0);
		if (subrsRel == 0)
			return (null, 0);

		var local = CffIndex.Read(data, file.At(privOffset + subrsRel));
		return (local, Bias(local.Count));
	}

	private static (CffIndex?[] Local, int[] Bias, byte[] FdSelect) ReadCidSubrs(
		byte[] data, SfntReader file, int cffOffset, CffDict topDict, int numGlyphs)
	{
		// FDArray: an INDEX of font DICTs, each with its own Private DICT → local subrs.
		var fdArrayOffset = cffOffset + (int)topDict.GetSingle(1236, 0);
		var fdArray = CffIndex.Read(data, file.At(fdArrayOffset));
		var local = new CffIndex?[fdArray.Count];
		var bias = new int[fdArray.Count];
		for (var i = 0; i < fdArray.Count; i++)
		{
			var fontDict = CffDict.Parse(fdArray.Get(i));
			var (l, b) = ReadPrivateLocalSubrs(data, file, cffOffset, fontDict);
			local[i] = l;
			bias[i] = b;
		}

		var fdSelect = ReadFdSelect(file, cffOffset + (int)topDict.GetSingle(1237, 0), numGlyphs);
		return (local, bias, fdSelect);
	}

	private static byte[] ReadFdSelect(SfntReader file, int offset, int numGlyphs)
	{
		var map = new byte[numGlyphs];
		var r = file.At(offset);
		int format = r.ReadByte();
		if (format == 0)
		{
			for (var i = 0; i < numGlyphs; i++)
				map[i] = r.ReadByte();
		}
		else if (format == 3)
		{
			int nRanges = r.ReadUInt16();
			int first = r.ReadUInt16();
			for (var i = 0; i < nRanges; i++)
			{
				int fd = r.ReadByte();
				int next = r.ReadUInt16();
				for (var g = first; g < next && g < numGlyphs; g++)
					map[g] = (byte)fd;
				first = next;
			}
		}

		return map;
	}

	/// <summary>Stateful Type2 charstring interpreter building one <see cref="Shape"/>.</summary>
	private sealed class Type2Interpreter(CffIndex globalSubrs, int globalBias, CffIndex? localSubrs, int localBias)
	{
		private readonly double[] _stack = new double[48];
		private int _sp;
		private double _x;
		private double _y;
		private int _nStems;
		private bool _haveWidth;
		private bool _open;
		private Contour _contour = new();
		private Vector2D _start;
		private Shape _shape = null!;
		private bool _done;

		public void Run(byte[] charstring, Shape shape)
		{
			_shape = shape;
			Execute(charstring, 0);
			CloseContour();
		}

		private void Execute(byte[] cs, int depth)
		{
			if (depth > SubrDepthLimit)
				return;

			var i = 0;
			while (i < cs.Length && !_done)
			{
				int b0 = cs[i++];
				if (b0 >= 32 || b0 == 28)
				{
					i = PushOperand(cs, i, b0);
					continue;
				}

				switch (b0)
				{
					case 1:
					case 3:
					case 18:
					case 23: // h/v stem (hm)
						CountStems();
						break;
					case 19:
					case 20: // hintmask / cntrmask
						CountStems();
						i += (_nStems + 7) / 8;
						break;
					case 21: // rmoveto
						MoveTo(2);
						break;
					case 22: // hmoveto
						MoveTo(1, horizontal: true);
						break;
					case 4: // vmoveto
						MoveTo(1, horizontal: false, vertical: true);
						break;
					case 5: // rlineto
						RLineTo();
						break;
					case 6: // hlineto
						AlternatingLine(startHorizontal: true);
						break;
					case 7: // vlineto
						AlternatingLine(startHorizontal: false);
						break;
					case 8: // rrcurveto
						RrCurveTo(0);
						_sp = 0;
						break;
					case 24: // rcurveline
						RCurveLine();
						break;
					case 25: // rlinecurve
						RLineCurve();
						break;
					case 26: // vvcurveto
						VvCurveTo();
						break;
					case 27: // hhcurveto
						HhCurveTo();
						break;
					case 30: // vhcurveto
						AlternatingCurve(startHorizontal: false);
						break;
					case 31: // hvcurveto
						AlternatingCurve(startHorizontal: true);
						break;
					case 10: // callsubr
						CallSubr(localSubrs, localBias, depth);
						break;
					case 29: // callgsubr
						CallSubr(globalSubrs, globalBias, depth);
						break;
					case 11: // return
						return;
					case 14: // endchar
						_done = true;
						return;
					case 12: // escape (flex family)
						Escape(cs[i++]);
						break;
					default:
						_sp = 0; // unknown — drop operands defensively
						break;
				}
			}
		}

		private int PushOperand(byte[] cs, int i, int b0)
		{
			double value;
			if (b0 == 28)
			{
				value = (short)((cs[i] << 8) | cs[i + 1]);
				i += 2;
			}
			else if (b0 < 247)
			{
				value = b0 - 139;
			}
			else if (b0 < 251)
			{
				value = (b0 - 247) * 256 + cs[i] + 108;
				i += 1;
			}
			else if (b0 < 255)
			{
				value = -(b0 - 251) * 256 - cs[i] - 108;
				i += 1;
			}
			else // 255: 16.16 fixed
			{
				var v = (cs[i] << 24) | (cs[i + 1] << 16) | (cs[i + 2] << 8) | cs[i + 3];
				value = v / 65536.0;
				i += 4;
			}

			if (_sp < _stack.Length)
				_stack[_sp++] = value;
			return i;
		}

		private void Escape(int op)
		{
			switch (op)
			{
				case 34: HFlex(); break;
				case 35: Flex(); break;
				case 36: HFlex1(); break;
				case 37: Flex1(); break;
				default: _sp = 0; break;
			}
		}

		private void CountStems()
		{
			if (!_haveWidth && (_sp & 1) == 1)
				_haveWidth = true; // leading width operand
			_nStems += _sp / 2;
			_sp = 0;
		}

		private void MoveTo(int args, bool horizontal = false, bool vertical = false)
		{
			var b = 0;
			if (!_haveWidth && _sp > args)
				b = 1;
			_haveWidth = true;

			if (args == 2)
			{
				_x += _stack[b];
				_y += _stack[b + 1];
			}
			else if (horizontal)
			{
				_x += _stack[b];
			}
			else if (vertical)
			{
				_y += _stack[b];
			}

			BeginContour();
			_sp = 0;
		}

		private void RLineTo()
		{
			for (var i = 0; i + 1 < _sp; i += 2)
			{
				_x += _stack[i];
				_y += _stack[i + 1];
				LineTo();
			}

			_sp = 0;
		}

		private void AlternatingLine(bool startHorizontal)
		{
			var horiz = startHorizontal;
			for (var i = 0; i < _sp; i++)
			{
				if (horiz)
					_x += _stack[i];
				else
					_y += _stack[i];
				LineTo();
				horiz = !horiz;
			}

			_sp = 0;
		}

		// Emit consecutive rrcurveto groups of 6 starting at index `from`; returns the next index.
		private int RrCurveTo(int from)
		{
			var i = from;
			while (i + 6 <= _sp)
			{
				var c1 = new Vector2D(_x + _stack[i], _y + _stack[i + 1]);
				var c2 = new Vector2D(c1.X + _stack[i + 2], c1.Y + _stack[i + 3]);
				var end = new Vector2D(c2.X + _stack[i + 4], c2.Y + _stack[i + 5]);
				CurveTo(c1, c2, end);
				i += 6;
			}

			return i;
		}

		private void RCurveLine()
		{
			var i = 0;
			while (i + 6 <= _sp - 2)
			{
				var c1 = new Vector2D(_x + _stack[i], _y + _stack[i + 1]);
				var c2 = new Vector2D(c1.X + _stack[i + 2], c1.Y + _stack[i + 3]);
				var end = new Vector2D(c2.X + _stack[i + 4], c2.Y + _stack[i + 5]);
				CurveTo(c1, c2, end);
				i += 6;
			}

			_x += _stack[i];
			_y += _stack[i + 1];
			LineTo();
			_sp = 0;
		}

		private void RLineCurve()
		{
			var i = 0;
			while (i + 2 <= _sp - 6)
			{
				_x += _stack[i];
				_y += _stack[i + 1];
				LineTo();
				i += 2;
			}

			var c1 = new Vector2D(_x + _stack[i], _y + _stack[i + 1]);
			var c2 = new Vector2D(c1.X + _stack[i + 2], c1.Y + _stack[i + 3]);
			var end = new Vector2D(c2.X + _stack[i + 4], c2.Y + _stack[i + 5]);
			CurveTo(c1, c2, end);
			_sp = 0;
		}

		private void VvCurveTo()
		{
			var i = 0;
			double dx1 = 0;
			if ((_sp & 1) != 0)
			{
				dx1 = _stack[0];
				i = 1;
			}

			while (i + 4 <= _sp)
			{
				var c1 = new Vector2D(_x + dx1, _y + _stack[i]);
				var c2 = new Vector2D(c1.X + _stack[i + 1], c1.Y + _stack[i + 2]);
				var end = new Vector2D(c2.X, c2.Y + _stack[i + 3]);
				CurveTo(c1, c2, end);
				dx1 = 0;
				i += 4;
			}

			_sp = 0;
		}

		private void HhCurveTo()
		{
			var i = 0;
			double dy1 = 0;
			if ((_sp & 1) != 0)
			{
				dy1 = _stack[0];
				i = 1;
			}

			while (i + 4 <= _sp)
			{
				var c1 = new Vector2D(_x + _stack[i], _y + dy1);
				var c2 = new Vector2D(c1.X + _stack[i + 1], c1.Y + _stack[i + 2]);
				var end = new Vector2D(c2.X + _stack[i + 3], c2.Y);
				CurveTo(c1, c2, end);
				dy1 = 0;
				i += 4;
			}

			_sp = 0;
		}

		private void AlternatingCurve(bool startHorizontal)
		{
			var i = 0;
			var horiz = startHorizontal;
			while (i + 4 <= _sp)
			{
				var last = _sp - i == 5;
				Vector2D c1, c2, end;
				if (horiz)
				{
					c1 = new Vector2D(_x + _stack[i], _y);
					c2 = new Vector2D(c1.X + _stack[i + 1], c1.Y + _stack[i + 2]);
					var ey = c2.Y + _stack[i + 3];
					var ex = last ? c2.X + _stack[i + 4] : c2.X;
					end = new Vector2D(ex, ey);
				}
				else
				{
					c1 = new Vector2D(_x, _y + _stack[i]);
					c2 = new Vector2D(c1.X + _stack[i + 1], c1.Y + _stack[i + 2]);
					var ex = c2.X + _stack[i + 3];
					var ey = last ? c2.Y + _stack[i + 4] : c2.Y;
					end = new Vector2D(ex, ey);
				}

				CurveTo(c1, c2, end);
				horiz = !horiz;
				i += 4;
			}

			_sp = 0;
		}

		private void Flex()
		{
			var c1 = new Vector2D(_x + _stack[0], _y + _stack[1]);
			var c2 = new Vector2D(c1.X + _stack[2], c1.Y + _stack[3]);
			var p3 = new Vector2D(c2.X + _stack[4], c2.Y + _stack[5]);
			CurveTo(c1, c2, p3);
			var c4 = new Vector2D(p3.X + _stack[6], p3.Y + _stack[7]);
			var c5 = new Vector2D(c4.X + _stack[8], c4.Y + _stack[9]);
			var p6 = new Vector2D(c5.X + _stack[10], c5.Y + _stack[11]);
			CurveTo(c4, c5, p6);
			_sp = 0;
		}

		private void HFlex()
		{
			var startY = _y;
			var c1 = new Vector2D(_x + _stack[0], _y);
			var c2 = new Vector2D(c1.X + _stack[1], c1.Y + _stack[2]);
			var p3 = new Vector2D(c2.X + _stack[3], c2.Y);
			CurveTo(c1, c2, p3);
			var c4 = new Vector2D(p3.X + _stack[4], p3.Y);
			var c5 = new Vector2D(c4.X + _stack[5], startY);
			var p6 = new Vector2D(c5.X + _stack[6], startY);
			CurveTo(c4, c5, p6);
			_sp = 0;
		}

		private void HFlex1()
		{
			var startY = _y;
			var c1 = new Vector2D(_x + _stack[0], _y + _stack[1]);
			var c2 = new Vector2D(c1.X + _stack[2], c1.Y + _stack[3]);
			var p3 = new Vector2D(c2.X + _stack[4], c2.Y);
			CurveTo(c1, c2, p3);
			var c4 = new Vector2D(p3.X + _stack[5], p3.Y);
			var c5 = new Vector2D(c4.X + _stack[6], c4.Y + _stack[7]);
			var p6 = new Vector2D(c5.X + _stack[8], startY);
			CurveTo(c4, c5, p6);
			_sp = 0;
		}

		private void Flex1()
		{
			var startX = _x;
			var startY = _y;
			var dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
			var dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
			var c1 = new Vector2D(_x + _stack[0], _y + _stack[1]);
			var c2 = new Vector2D(c1.X + _stack[2], c1.Y + _stack[3]);
			var p3 = new Vector2D(c2.X + _stack[4], c2.Y + _stack[5]);
			CurveTo(c1, c2, p3);
			var c4 = new Vector2D(p3.X + _stack[6], p3.Y + _stack[7]);
			var c5 = new Vector2D(c4.X + _stack[8], c4.Y + _stack[9]);
			var p6 = Math.Abs(dx) > Math.Abs(dy)
				? new Vector2D(c5.X + _stack[10], startY)
				: new Vector2D(startX, c5.Y + _stack[10]);
			CurveTo(c4, c5, p6);
			_sp = 0;
		}

		private void CallSubr(CffIndex? subrs, int bias, int depth)
		{
			if (subrs is null || _sp == 0)
			{
				_sp = 0;
				return;
			}

			var index = (int)_stack[--_sp] + bias;
			if (index >= 0 && index < subrs.Count)
				Execute(subrs.Get(index), depth + 1);
		}

		private void BeginContour()
		{
			CloseContour();
			_contour = new Contour();
			_start = new Vector2D(_x, _y);
			_open = true;
		}

		private void LineTo()
		{
			if (_open)
				_contour.Add(new LinearSegment(LastPoint(), new Vector2D(_x, _y)));
		}

		private void CurveTo(Vector2D c1, Vector2D c2, Vector2D end)
		{
			if (_open)
				_contour.Add(new CubicSegment(LastPoint(), c1, c2, end));
			_x = end.X;
			_y = end.Y;
		}

		private Vector2D LastPoint() =>
			_contour.Edges.Count > 0 ? _contour.Edges[^1].Point(1) : _start;

		private void CloseContour()
		{
			if (!_open)
				return;
			_open = false;
			if (_contour.Edges.Count == 0)
				return;

			var last = _contour.Edges[^1].Point(1);
			if (last.X != _start.X || last.Y != _start.Y)
				_contour.Add(new LinearSegment(last, _start));
			_shape.Contours.Add(_contour);
		}
	}
}
