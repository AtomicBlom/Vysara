using System.Numerics;
using Vysara.Drawing;

namespace Vysara.Rasterizer;

/// <summary>
/// A sampled texture for the rasterizer — RGBA8 texels with clamp-address bilinear filtering, returning linear
/// channels in 0..1. Mirrors ui.slang's per-command <c>Sampler2D</c> bound through the texture format its source declares:
/// an <see cref="TextureColorSpace.Srgb"/> texture decodes each texel's colour to linear light before filtering, as an
/// sRGB format's hardware decode does, and a <see cref="TextureColorSpace.Linear"/> one (a font MTSDF atlas, whose
/// texels are signed distances) is read raw. Alpha is never decoded. <see cref="Sample"/> returns what the hardware
/// returns; premultiplying a <see cref="TextureAlphaMode.Straight"/> texture's sample is the shader's step, after it.
/// </summary>
#if VYSARA_RASTERIZER_INTERNAL
internal
#else
public
#endif
sealed class Texture
{
	// RGB byte → linear value, per colour space. Linear is byte / 255, bit for bit what dividing the texel by 255 gives.
	private static readonly float[] LinearBytes = BuildTable(TextureColorSpace.Linear);
	private static readonly float[] SrgbBytes = BuildTable(TextureColorSpace.Srgb);

	private readonly byte[] _rgba;
	private readonly float[] _decode;

	public int Width { get; }
	public int Height { get; }

	/// <summary>How the RGB bytes decode to linear light.</summary>
	public TextureColorSpace ColorSpace { get; }

	/// <summary>Whether a sample's colour is premultiplied, or straight and to be premultiplied by its alpha.</summary>
	public TextureAlphaMode AlphaMode { get; }

	public Texture(int width, int height, byte[] rgba,
		TextureColorSpace colorSpace = TextureColorSpace.Linear, TextureAlphaMode alphaMode = TextureAlphaMode.Premultiplied)
	{
		if (rgba.Length < width * height * 4)
			throw new ArgumentException("pixel buffer too small for the given dimensions", nameof(rgba));
		Width = width;
		Height = height;
		_rgba = rgba;
		ColorSpace = colorSpace;
		AlphaMode = alphaMode;
		_decode = colorSpace == TextureColorSpace.Srgb ? SrgbBytes : LinearBytes;
	}

	/// <summary>Copy a <see cref="TextureSource"/>'s RGBA8 texels, read as it declares: a glyph atlas page, an image, or
	/// the white texel.</summary>
	public static Texture FromSource(TextureSource source) =>
		new(source.Width, source.Height, source.Pixels.ToArray(), source.ColorSpace, source.AlphaMode);

	/// <summary>Copy a newer version of <paramref name="source"/>'s texels over this texture's, with no allocation, when
	/// it is the size and reads the way this texture does; false, leaving the texture as it was, when it is not.</summary>
	internal bool TryUpdate(TextureSource source)
	{
		var pixels = source.Pixels.Span;
		if (source.Width != Width || source.Height != Height || source.ColorSpace != ColorSpace || source.AlphaMode != AlphaMode
			|| pixels.Length != _rgba.Length)
		{
			return false;
		}
		pixels.CopyTo(_rgba);
		return true;
	}

	/// <summary>Bilinear sample at <paramref name="uv"/> (0..1, v=0 at the top), clamp addressing: linear RGBA in 0..1,
	/// each texel decoded before the filtering.</summary>
	public Vector4 Sample(Vector2 uv)
	{
		// Half-texel offset: uv*size lands on texel corners; -0.5 centres the bilinear footprint on texel centres.
		var fx = uv.X * Width - 0.5f;
		var fy = uv.Y * Height - 0.5f;
		var x0 = (int)MathF.Floor(fx);
		var y0 = (int)MathF.Floor(fy);
		var tx = fx - x0;
		var ty = fy - y0;

		var top = Vector4.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), tx);
		var bot = Vector4.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), tx);
		return Vector4.Lerp(top, bot, ty);
	}

	private Vector4 Texel(int x, int y)
	{
		x = Math.Clamp(x, 0, Width - 1);
		y = Math.Clamp(y, 0, Height - 1);
		var o = (y * Width + x) * 4;
		return new Vector4(_decode[_rgba[o]], _decode[_rgba[o + 1]], _decode[_rgba[o + 2]], _rgba[o + 3] / 255f);
	}

	// The 256 linear values an RGB byte decodes to: the byte over 255, through the sRGB transfer function for sRGB (IEC
	// 61966-2-1, evaluated in double and rounded once, as a hardware sRGB decode's table is).
	private static float[] BuildTable(TextureColorSpace colorSpace)
	{
		var table = new float[256];
		for (var i = 0; i < table.Length; i++)
		{
			if (colorSpace == TextureColorSpace.Linear)
			{
				table[i] = i / 255f;
				continue;
			}
			var c = i / 255.0;
			table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
		}
		return table;
	}
}
