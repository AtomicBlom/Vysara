using System.Buffers.Binary;

namespace Vysara.Text.OpenType;

/// <summary>
/// A big-endian cursor over an sfnt (TrueType/OpenType) file. All multi-byte OpenType values are big-endian; this
/// wraps the buffer with the handful of read primitives the table parsers need plus seek/branch helpers.
/// </summary>
internal sealed class SfntReader(byte[] data, int position = 0)
{
	private readonly byte[] _data = data;

	public int Position { get; set; } = position;

	public int Length => _data.Length;

	/// <summary>The underlying buffer (not copied) — for CFF parsing, which slices DICT/INDEX byte ranges directly.</summary>
	public byte[] Raw => _data;

	public byte ReadByte() => _data[Position++];

	public sbyte ReadSByte() => (sbyte)_data[Position++];

	public ushort ReadUInt16()
	{
		var v = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(Position));
		Position += 2;
		return v;
	}

	public short ReadInt16()
	{
		var v = BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(Position));
		Position += 2;
		return v;
	}

	public uint ReadUInt32()
	{
		var v = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(Position));
		Position += 4;
		return v;
	}

	public int ReadInt32()
	{
		var v = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(Position));
		Position += 4;
		return v;
	}

	/// <summary>Read a 4-char ASCII table tag without allocating per-tag.</summary>
	public string ReadTag()
	{
		var tag = System.Text.Encoding.ASCII.GetString(_data, Position, 4);
		Position += 4;
		return tag;
	}

	public void Skip(int bytes) => Position += bytes;

	/// <summary>A fresh cursor over the same buffer starting at <paramref name="offset"/>.</summary>
	public SfntReader At(long offset) => new(_data, checked((int)offset));
}
