using Vysara.Drawing;

namespace Vysara.Media.Imaging;

/// <summary>
/// An image whose texels the app writes, as WinUI's <c>WriteableBitmap</c>: change <see cref="PixelBuffer"/>, then call
/// <see cref="Invalidate"/> to show the change.
/// <para>
/// <see cref="Invalidate"/> moves the image's <see cref="TextureSource.Version"/>, so every surface drawing it moves its
/// <see cref="DrawListData.TextureRevision"/> on its next frame, with no layout and no recomposite, and a host brings its
/// texture up to date. A host that can update a texture in place writes the new texels into the one it has, ordered after
/// the frames still sampling it (Loom's painter does, through <c>IEngine.UpdateTexture</c>); one that cannot uploads them as
/// a new texture and releases the old one once the frames in flight are done with it. Nothing on Vysara's side allocates,
/// so a bitmap can be rewritten every frame; whether the host's side does is the host's.
/// </para>
/// <para>
/// Unlike WinUI's, whose buffer is BGRA8 with premultiplied alpha, the buffer is RGBA8, like every
/// <see cref="TextureSource"/>, and is by default sRGB-encoded colour with straight alpha; the constructor that takes an
/// alpha mode and a colour space is Vysara's.
/// </para>
/// </summary>
public sealed class WriteableBitmap : BitmapSource
{
	/// <summary>A transparent image of <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> texels, sRGB-encoded
	/// with straight alpha.</summary>
	public WriteableBitmap(int pixelWidth, int pixelHeight)
		: this(pixelWidth, pixelHeight, TextureAlphaMode.Straight, TextureColorSpace.Srgb)
	{
	}

	/// <summary>A transparent image of <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> texels whose bytes
	/// the app writes in <paramref name="colorSpace"/>, with <paramref name="alphaMode"/> alpha. Vysara-only.</summary>
	public WriteableBitmap(int pixelWidth, int pixelHeight, TextureAlphaMode alphaMode, TextureColorSpace colorSpace)
		: base(pixelWidth, pixelHeight, new byte[TexelBytes(pixelWidth, pixelHeight)], alphaMode, colorSpace)
	{
	}

	// The bytes of a pixelWidth × pixelHeight RGBA8 buffer, refusing a size no array can hold.
	private static int TexelBytes(int pixelWidth, int pixelHeight)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
		var bytes = (long)pixelWidth * pixelHeight * 4;
		if (bytes > Array.MaxLength)
			throw new ArgumentOutOfRangeException(nameof(pixelWidth), $"{pixelWidth}x{pixelHeight} RGBA8 texels need {bytes} bytes, more than an array holds.");
		return (int)bytes;
	}

	/// <summary>The texels to write: RGBA8, row-major, <see cref="BitmapSource.PixelWidth"/> ×
	/// <see cref="BitmapSource.PixelHeight"/> × 4 bytes. A change shows after <see cref="Invalidate"/>.</summary>
	public Memory<byte> PixelBuffer => Texels;

	/// <summary>Show what was written to <see cref="PixelBuffer"/>: the image's version moves, and a host replaces its
	/// texture at its next frame. Allocates nothing.</summary>
	/// <exception cref="ObjectDisposedException">The bitmap has been disposed.</exception>
	public void Invalidate()
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);
		InvalidateTexels();
	}
}
