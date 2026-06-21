namespace SUIsei.Rasterizer;

/// <summary>
/// A dependency-free BMP encoder for the spike — 32-bit top-down BGRA, BI_RGB (no compression). Just enough to eye
/// the rasterizer output in any image viewer; a real golden pipeline would write PNG. Input is the RGBA8 buffer
/// from <see cref="SoftwareRasterizer.ResolveToRgba8"/>.
/// </summary>
public static class BmpWriter
{
	public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgba)
	{
		var imageBytes = width * height * 4;
		const int headerBytes = 14 + 40;
		var buf = new byte[headerBytes + imageBytes];
		var o = 0;

		// BITMAPFILEHEADER
		buf[o++] = (byte)'B';
		buf[o++] = (byte)'M';
		WriteI32(buf, ref o, buf.Length); // file size
		WriteI32(buf, ref o, 0);          // reserved
		WriteI32(buf, ref o, headerBytes); // offset to pixels

		// BITMAPINFOHEADER
		WriteI32(buf, ref o, 40);          // header size
		WriteI32(buf, ref o, width);
		WriteI32(buf, ref o, -height);     // negative height → rows are top-down
		WriteI16(buf, ref o, 1);           // planes
		WriteI16(buf, ref o, 32);          // bits per pixel
		WriteI32(buf, ref o, 0);           // BI_RGB
		WriteI32(buf, ref o, imageBytes);
		WriteI32(buf, ref o, 2835);        // ~72 DPI (x)
		WriteI32(buf, ref o, 2835);        // ~72 DPI (y)
		WriteI32(buf, ref o, 0);           // palette colours
		WriteI32(buf, ref o, 0);           // important colours

		for (var i = 0; i < width * height; i++)
		{
			var s = i * 4;
			buf[o++] = rgba[s + 2]; // B
			buf[o++] = rgba[s + 1]; // G
			buf[o++] = rgba[s + 0]; // R
			buf[o++] = rgba[s + 3]; // A
		}

		File.WriteAllBytes(path, buf);
	}

	private static void WriteI32(byte[] b, ref int o, int v)
	{
		b[o++] = (byte)v;
		b[o++] = (byte)(v >> 8);
		b[o++] = (byte)(v >> 16);
		b[o++] = (byte)(v >> 24);
	}

	private static void WriteI16(byte[] b, ref int o, int v)
	{
		b[o++] = (byte)v;
		b[o++] = (byte)(v >> 8);
	}
}
