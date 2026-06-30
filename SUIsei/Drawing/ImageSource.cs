using StbImageSharp;

namespace SUIsei.Drawing;

/// <summary>
/// A decoded RGBA8 image an <see cref="Controls.Image"/> draws. SUIsei stays GPU-free: the
/// source holds neutral pixels (decoded with StbImageSharp, the same decoder as <see cref="FontAtlas"/>) and a
/// stable <see cref="TextureIndex"/>; the engine glue uploads it once and binds it by that index, the same texture
/// channel the white texel (0) and glyph atlas (1) ride. Indices start at 2 so they never collide with those two.
/// </summary>
public sealed class ImageSource
{
	private static int _next = 1; // 0 = white texel, 1 = glyph atlas — reserved by the painter; images start at 2.

	private ImageSource(AtlasImage pixels)
	{
		Pixels = pixels;
		TextureIndex = Interlocked.Increment(ref _next);
	}

	/// <summary>The texture-channel index this image binds to (≥ 2; stable for the source's lifetime).</summary>
	public int TextureIndex { get; }

	/// <summary>The decoded RGBA8 pixels.</summary>
	public AtlasImage Pixels { get; }

	public int Width => Pixels.Width;

	public int Height => Pixels.Height;

	/// <summary>Wrap already-decoded RGBA8 pixels (e.g. a procedural or test image).</summary>
	public static ImageSource FromRgba8(int width, int height, byte[] rgba) => new(new AtlasImage(width, height, rgba));

	/// <summary>Decode an encoded image (PNG/JPG/…) to RGBA8 with StbImageSharp.</summary>
	public static ImageSource FromBytes(ReadOnlySpan<byte> encoded)
	{
		var result = ImageResult.FromMemory(encoded.ToArray(), ColorComponents.RedGreenBlueAlpha);
		return new ImageSource(new AtlasImage(result.Width, result.Height, result.Data));
	}
}
