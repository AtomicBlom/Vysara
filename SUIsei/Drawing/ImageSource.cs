using StbImageSharp;

namespace SUIsei.Drawing;

/// <summary>
/// A decoded RGBA8 image an <see cref="Controls.Image"/> draws. SUIsei stays GPU-free: the source holds neutral pixels
/// (decoded with StbImageSharp, the same decoder as <see cref="FontAtlas"/>), and as a <see cref="TextureSource"/> it
/// reaches the host through the slot table of every draw list that draws it; the host uploads it once and binds it by
/// slot.
/// </summary>
public sealed class ImageSource : TextureSource
{
	private readonly byte[] _rgba;

	private ImageSource(int width, int height, byte[] rgba)
	{
		Width = width;
		Height = height;
		_rgba = rgba;
	}

	public override int Width { get; }

	public override int Height { get; }

	/// <summary>The decoded RGBA8 pixels.</summary>
	public override ReadOnlyMemory<byte> Pixels => _rgba;

	/// <summary>Wrap already-decoded RGBA8 pixels (e.g. a procedural or test image).</summary>
	public static ImageSource FromRgba8(int width, int height, byte[] rgba) => new(width, height, rgba);

	/// <summary>Decode an encoded image (PNG/JPG/…) to RGBA8 with StbImageSharp.</summary>
	public static ImageSource FromBytes(ReadOnlySpan<byte> encoded)
	{
		var result = ImageResult.FromMemory(encoded.ToArray(), ColorComponents.RedGreenBlueAlpha);
		return new ImageSource(result.Width, result.Height, result.Data);
	}
}
