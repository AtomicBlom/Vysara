using System.Buffers.Binary;
using System.IO.Compression;

namespace Vysara.Text.Atlas;

/// <summary>
/// A dependency-free RGBA8 PNG encoder. Uses the built-in <see cref="ZLibStream"/> for the IDAT deflate (no native
/// or third-party code) and a hand-rolled CRC-32 for the chunk checksums. The MTSDF page is linear data, so it is
/// written as a plain truecolour-with-alpha PNG with no colour-space chunks (the loader decodes it UNORM/linear).
/// </summary>
internal static class PngWriter
{
	private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
	private static readonly uint[] CrcTable = BuildCrcTable();

	/// <summary>Encode top-down RGBA8 pixels (width·height·4 bytes) to a PNG byte buffer.</summary>
	public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
	{
		using var output = new MemoryStream();
		output.Write(Signature);

		// IHDR
		var ihdr = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
		ihdr[8] = 8;  // bit depth
		ihdr[9] = 6;  // colour type: truecolour + alpha
		ihdr[10] = 0; // compression: deflate
		ihdr[11] = 0; // filter: adaptive
		ihdr[12] = 0; // interlace: none
		WriteChunk(output, "IHDR", ihdr);

		// IDAT — filter byte 0 (None) per scanline, then zlib-compress the lot.
		var stride = width * 4;
		using var raw = new MemoryStream((stride + 1) * height);
		for (var y = 0; y < height; y++)
		{
			raw.WriteByte(0);
			raw.Write(rgba.Slice(y * stride, stride));
		}

		using var compressed = new MemoryStream();
		using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
			z.Write(raw.GetBuffer(), 0, (int)raw.Length);
		WriteChunk(output, "IDAT", compressed.ToArray());

		WriteChunk(output, "IEND", []);
		return output.ToArray();
	}

	private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
	{
		Span<byte> header = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
		output.Write(header);

		Span<byte> typeBytes = stackalloc byte[4];
		for (var i = 0; i < 4; i++)
			typeBytes[i] = (byte)type[i];
		output.Write(typeBytes);
		output.Write(data);

		var crc = Crc32(typeBytes, data);
		BinaryPrimitives.WriteUInt32BigEndian(header, crc);
		output.Write(header);
	}

	private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var x in a)
			crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
		foreach (var x in b)
			crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
		return crc ^ 0xFFFFFFFFu;
	}

	private static uint[] BuildCrcTable()
	{
		var table = new uint[256];
		for (var n = 0u; n < 256; n++)
		{
			var c = n;
			for (var k = 0; k < 8; k++)
				c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			table[n] = c;
		}

		return table;
	}
}
