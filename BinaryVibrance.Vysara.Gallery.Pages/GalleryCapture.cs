using BinaryVibrance.Vysara.Rasterizer;
using BinaryVibrance.Vysara.Text.Atlas;

namespace BinaryVibrance.Vysara.Gallery;

/// <summary>A frame the gallery drew without a window: straight RGBA8, <see cref="Width"/> × <see cref="Height"/>.</summary>
internal sealed record GalleryCapture(int Width, int Height, byte[] Rgba)
{
	/// <summary>The width a page is captured at: the page area beside the shell's list in its default 960-wide window.</summary>
	public const int PageWidth = 760;

	/// <summary>How many frames a capture steps through before it is taken, a sixtieth of a second apart, so a visual
	/// state entered on the first frame has finished its transition.</summary>
	public const int SettleFrames = 30;

	/// <summary>
	/// <paramref name="page"/> drawn on its own, <paramref name="pixelWidth"/> wide and as tall as it lays out, after
	/// <paramref name="frames"/> frames. The same page always draws the same pixels: time is the frame count, not the clock.
	/// </summary>
	public static GalleryCapture Page(GalleryPage page, int pixelWidth = PageWidth, float scale = 1f, int frames = SettleFrames)
	{
		using var renderer = new GalleryRenderer(page.Create()) { Scale = scale };
		var height = renderer.DesiredHeight(pixelWidth);
		return new(pixelWidth, height, Frames(renderer, pixelWidth, height, frames));
	}

	/// <summary>The whole gallery, its shell showing <paramref name="page"/> (or the first page), at a window's
	/// size. <paramref name="then"/>, when given, acts on the shell after its first frame, laid out in its window, as
	/// a user would: opening a menu, say.</summary>
	public static GalleryCapture Shell(string? page, int pixelWidth, int pixelHeight, float scale = 1f, int frames = 1, Action<GalleryShell>? then = null)
	{
		var shell = new GalleryShell(page);
		using var renderer = new GalleryRenderer(shell) { Scale = scale };
		return new(pixelWidth, pixelHeight, Frames(renderer, pixelWidth, pixelHeight, frames, then is null ? null : () => then(shell)));
	}

	/// <summary>Write the capture to <paramref name="path"/>: a PNG when it ends <c>.png</c>, else a BMP.</summary>
	public void Save(string path)
	{
		if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			File.WriteAllBytes(path, PngWriter.Encode(Width, Height, Rgba));
		else
			BmpWriter.Write(path, Width, Height, Rgba);
	}

	// Compose every frame but the last, which is also rasterized: only the last is kept. `afterFirst` runs once the
	// first frame has laid the content out.
	private static byte[] Frames(GalleryRenderer renderer, int width, int height, int frames, Action? afterFirst = null)
	{
		var last = Math.Max(afterFirst is null ? 1 : 2, frames) - 1;
		for (var i = 0; i < last; i++)
		{
			renderer.Compose(width, height, i / 60.0);
			if (i == 0)
				afterFirst?.Invoke();
		}
		return renderer.Render(width, height, last / 60.0);
	}
}
