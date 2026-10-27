using System.Numerics;
using SUIsei.Drawing;

namespace SUIsei.Rasterizer;

/// <summary>
/// A sampled texture for the rasterizer — RGBA8 texels with clamp-address bilinear filtering, returning linear
/// channels in 0..1. Mirrors ui.slang's per-command <c>Sampler2D</c>. Font MTSDF atlases bind here as linear/UNORM:
/// the texels are signed distances, never sRGB, so they are read raw (no gamma decode) — matching how the engine
/// bound the atlas page.
/// </summary>
#if SUISEI_RASTERIZER_INTERNAL
internal
#else
public
#endif
sealed class Texture
{
	private readonly byte[] _rgba;

	public int Width { get; }
	public int Height { get; }

	public Texture(int width, int height, byte[] rgba)
	{
		if (rgba.Length < width * height * 4)
			throw new ArgumentException("pixel buffer too small for the given dimensions", nameof(rgba));
		Width = width;
		Height = height;
		_rgba = rgba;
	}

	/// <summary>Copy a <see cref="TextureSource"/>'s RGBA8 texels: a glyph atlas page, an image, or the white texel.</summary>
	public static Texture FromSource(TextureSource source) => new(source.Width, source.Height, source.Pixels.ToArray());

	/// <summary>Bilinear sample at <paramref name="uv"/> (0..1, v=0 at the top), clamp addressing. Linear RGBA in 0..1.</summary>
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
		return new Vector4(_rgba[o], _rgba[o + 1], _rgba[o + 2], _rgba[o + 3]) / 255f;
	}
}
