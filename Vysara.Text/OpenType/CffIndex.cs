namespace Vysara.Text.OpenType;

/// <summary>
/// A CFF INDEX: a count-prefixed array of variable-length data objects with an offset table. Used for the
/// charstrings, the global/local subroutines, and the name/string/top-dict arrays.
/// </summary>
internal sealed class CffIndex
{
	private readonly byte[] _data;
	private readonly int[] _offsets; // absolute file offsets, count+1 entries
	private readonly int _base;

	private CffIndex(byte[] data, int[] offsets, int baseOffset)
	{
		_data = data;
		_offsets = offsets;
		_base = baseOffset;
	}

	public int Count => _offsets.Length - 1;

	/// <summary>The byte range (absolute file offset + length) of element <paramref name="i"/>.</summary>
	public (int Start, int Length) Range(int i) => (_base + _offsets[i], _offsets[i + 1] - _offsets[i]);

	/// <summary>Read element <paramref name="i"/> into a fresh array.</summary>
	public byte[] Get(int i)
	{
		var (start, length) = Range(i);
		return _data.AsSpan(start, length).ToArray();
	}

	/// <summary>Parse an INDEX at <paramref name="reader"/>'s position and advance it past the INDEX.</summary>
	public static CffIndex Read(byte[] data, SfntReader reader)
	{
		int count = reader.ReadUInt16();
		if (count == 0)
			return new CffIndex(data, [0, 0], reader.Position);

		int offSize = reader.ReadByte();
		var offsets = new int[count + 1];
		for (var i = 0; i <= count; i++)
			offsets[i] = ReadOffset(reader, offSize);

		// Object data begins right after the offset array; offsets are 1-based from there.
		var dataBase = reader.Position - 1;
		reader.Position = dataBase + offsets[count];
		return new CffIndex(data, offsets, dataBase);
	}

	private static int ReadOffset(SfntReader r, int offSize)
	{
		var v = 0;
		for (var i = 0; i < offSize; i++)
			v = (v << 8) | r.ReadByte();
		return v;
	}
}
