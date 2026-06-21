using System.Numerics;
using SUIsei;

namespace SUIsei.Text.Tool;

/// <summary>
/// A tiny GPU-free MTSDF text renderer for eyeballing a baked atlas. Lays a single line out by advance and shades
/// each glyph with the same reconstruction the engine shader and <c>SUIsei.Rasterizer</c> use
/// (<c>median(rgb) − 0.5</c>, scaled by <c>screenPxRange</c>). Output is RGBA8, top-down, white text on a dark
/// background — purely a sanity preview, not a colour-accurate composite.
/// </summary>
internal static class PreviewRenderer
{
	public static (byte[] Rgba, int Width, int Height) Render(FontAtlas atlas, string text, float fontSizePx)
	{
		var padding = (int)MathF.Round(fontSizePx * 0.4f);
		var pen = (float)padding;
		var advance = 0f;
		foreach (var ch in text)
			advance += atlas.Advance(ch) * fontSizePx;
		var pixelWidth = Math.Max(1, (int)MathF.Ceiling(pen + advance + padding));
		var height = padding * 2 + (int)MathF.Ceiling(atlas.LineHeight * fontSizePx);
		var baseline = padding + (-atlas.Ascender) * fontSizePx;

		var bg = new Vector3(30, 33, 40);
		var fg = new Vector3(235, 238, 245);
		var rgba = new byte[pixelWidth * height * 4];
		for (var i = 0; i < pixelWidth * height; i++)
		{
			rgba[i * 4] = (byte)bg.X;
			rgba[i * 4 + 1] = (byte)bg.Y;
			rgba[i * 4 + 2] = (byte)bg.Z;
			rgba[i * 4 + 3] = 255;
		}

		var screenPxRange = atlas.ScreenPxRange(fontSizePx);
		var aw = atlas.Image.Width;
		var ah = atlas.Image.Height;
		var page = atlas.Image.Pixels.Span;

		foreach (var ch in text)
		{
			if (atlas.TryGetGlyph(ch, out var g) && g.HasGeometry)
			{
				var qx0 = pen + g.PlaneLeft * fontSizePx;
				var qy0 = baseline + g.PlaneTop * fontSizePx;
				var qx1 = pen + g.PlaneRight * fontSizePx;
				var qy1 = baseline + g.PlaneBottom * fontSizePx;

				var px0 = Math.Max(0, (int)MathF.Floor(qx0));
				var py0 = Math.Max(0, (int)MathF.Floor(qy0));
				var px1 = Math.Min(pixelWidth - 1, (int)MathF.Ceiling(qx1));
				var py1 = Math.Min(height - 1, (int)MathF.Ceiling(qy1));

				for (var y = py0; y <= py1; y++)
				{
					for (var x = px0; x <= px1; x++)
					{
						var u = Lerp(g.U0, g.U1, (x + 0.5f - qx0) / (qx1 - qx0));
						var v = Lerp(g.V0, g.V1, (y + 0.5f - qy0) / (qy1 - qy0));
						var s = SampleBilinear(page, aw, ah, u, v);
						var d = Median(s.X, s.Y, s.Z) - 0.5f;
						var cov = Math.Clamp(d * screenPxRange + 0.5f, 0f, 1f);
						if (cov <= 0f)
							continue;
						var o = (y * pixelWidth + x) * 4;
						var outColor = Vector3.Lerp(bg, fg, cov);
						rgba[o] = (byte)outColor.X;
						rgba[o + 1] = (byte)outColor.Y;
						rgba[o + 2] = (byte)outColor.Z;
					}
				}
			}

			pen += atlas.Advance(ch) * fontSizePx;
		}

		return (rgba, pixelWidth, height);
	}

	private static float Lerp(float a, float b, float t) => a + (b - a) * t;

	private static float Median(float a, float b, float c) => MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));

	private static Vector4 SampleBilinear(ReadOnlySpan<byte> page, int w, int h, float u, float v)
	{
		var fx = u * w - 0.5f;
		var fy = v * h - 0.5f;
		var x0 = (int)MathF.Floor(fx);
		var y0 = (int)MathF.Floor(fy);
		var tx = fx - x0;
		var ty = fy - y0;
		var top = Vector4.Lerp(Texel(page, w, h, x0, y0), Texel(page, w, h, x0 + 1, y0), tx);
		var bot = Vector4.Lerp(Texel(page, w, h, x0, y0 + 1), Texel(page, w, h, x0 + 1, y0 + 1), tx);
		return Vector4.Lerp(top, bot, ty);
	}

	private static Vector4 Texel(ReadOnlySpan<byte> page, int w, int h, int x, int y)
	{
		x = Math.Clamp(x, 0, w - 1);
		y = Math.Clamp(y, 0, h - 1);
		var o = (y * w + x) * 4;
		return new Vector4(page[o], page[o + 1], page[o + 2], page[o + 3]) / 255f;
	}
}
