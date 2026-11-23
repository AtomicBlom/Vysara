using StbImageSharp;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Media.Imaging;

/// <summary>
/// An image of decoded RGBA8 texels, as WinUI's <c>BitmapSource</c>. One made by <see cref="FromBytes"/> or
/// <see cref="FromRgba8"/> never changes; <see cref="WriteableBitmap"/> is the one whose texels the app rewrites. Vysara
/// stays GPU-free: the texels are neutral bytes (decoded with StbImageSharp, the decoder <see cref="FontAtlas"/> uses),
/// and reach the host through the slot table of every draw list that draws the image.
/// <para>
/// The factories are Vysara's (WinUI loads a bitmap from a stream or a URI). By default the texels are what an image file
/// holds: sRGB-encoded colour with straight alpha.
/// </para>
/// </summary>
public class BitmapSource : ImageSource
{
	private protected BitmapSource(int pixelWidth, int pixelHeight, byte[] rgba, TextureAlphaMode alphaMode, TextureColorSpace colorSpace)
		: base(alphaMode, colorSpace)
	{
		ArgumentNullException.ThrowIfNull(rgba);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
		if (rgba.Length != (long)pixelWidth * pixelHeight * 4)
			throw new ArgumentException($"{pixelWidth}x{pixelHeight} RGBA8 texels are {(long)pixelWidth * pixelHeight * 4} bytes, not {rgba.Length}.", nameof(rgba));
		Width = pixelWidth;
		Height = pixelHeight;
		Texels = rgba;
	}

	/// <summary>The image's width in texels.</summary>
	public int PixelWidth => Width;

	/// <summary>The image's height in texels.</summary>
	public int PixelHeight => Height;

	public override int Width { get; }

	public override int Height { get; }

	/// <summary>The decoded RGBA8 texels.</summary>
	public override ReadOnlyMemory<byte> Pixels => Texels;

	private protected byte[] Texels { get; }

	/// <summary>
	/// An image of already-decoded RGBA8 texels (a procedural or test image), row-major, <paramref name="width"/> ×
	/// <paramref name="height"/> × 4 bytes. The image keeps the array, so it must not change afterwards: an image whose
	/// texels change is a <see cref="WriteableBitmap"/>.
	/// </summary>
	public static BitmapSource FromRgba8(int width, int height, byte[] rgba,
		TextureAlphaMode alphaMode = TextureAlphaMode.Straight, TextureColorSpace colorSpace = TextureColorSpace.Srgb) =>
		new(width, height, rgba, alphaMode, colorSpace);

	/// <summary>Decode an encoded image (PNG, JPEG, BMP, TGA, …) to RGBA8 with StbImageSharp. Its texels are straight
	/// alpha; <paramref name="colorSpace"/> says whether its colour is sRGB-encoded (an ordinary image) or linear data.</summary>
	public static BitmapSource FromBytes(ReadOnlySpan<byte> encoded, TextureColorSpace colorSpace = TextureColorSpace.Srgb)
	{
		var result = ImageResult.FromMemory(encoded.ToArray(), ColorComponents.RedGreenBlueAlpha);
		return new BitmapSource(result.Width, result.Height, result.Data, TextureAlphaMode.Straight, colorSpace);
	}
}
