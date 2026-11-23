using BinaryVibrance.Vysara.Media.Imaging;

namespace BinaryVibrance.Vysara.Gallery.Pages;

/// <summary>Code-behind for <c>ImagesPage.xaml</c>: a procedural bitmap, and a live one <see cref="Animate"/> rewrites.</summary>
internal sealed partial class ImagesPage : IGalleryView
{
	public ImagesPage() => InitializeComponent();

	UIElement IGalleryView.Root => Root;

	/// <summary>x:Bind source for the first image: a procedurally generated gradient, so the page ships no asset.</summary>
	public BitmapSource LogoSource { get; } = MakeLogo();

	/// <summary>x:Bind source for the live image: a straight-alpha bitmap <see cref="Animate"/> rewrites every frame.</summary>
	public WriteableBitmap LiveBitmap { get; } = new(64, 40);

	/// <summary>
	/// Rewrite <see cref="LiveBitmap"/> for the time <paramref name="seconds"/>: rings rippling out of a disc whose rim
	/// fades out and whose troughs are half transparent, so its straight alpha blends over the card. The texels change in
	/// place and <see cref="WriteableBitmap.Invalidate"/> shows them, with no layout and no allocation.
	/// </summary>
	public void Animate(double seconds)
	{
		var bitmap = LiveBitmap;
		var pixels = bitmap.PixelBuffer.Span;
		var t = (float)seconds;
		for (var y = 0; y < bitmap.PixelHeight; y++)
		{
			for (var x = 0; x < bitmap.PixelWidth; x++)
			{
				var r = MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 19.5f) * (y - 19.5f));
				var ring = 0.5f + 0.5f * MathF.Cos(r * 0.6f - t * 5f);
				var rim = Math.Clamp((19.5f - r) / 5f, 0f, 1f);
				var o = (y * bitmap.PixelWidth + x) * 4;
				pixels[o] = (byte)(40f + 120f * ring);
				pixels[o + 1] = (byte)(200f - 110f * ring);
				pixels[o + 2] = (byte)(190f + 65f * ring);
				pixels[o + 3] = (byte)(255f * rim * (0.5f + 0.5f * ring));
			}
		}
		bitmap.Invalidate();
	}

	private static BitmapSource MakeLogo()
	{
		const int width = 64;
		const int height = 40;
		var pixels = new byte[width * height * 4];
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var o = (y * width + x) * 4;
				pixels[o] = (byte)(x * 255 / width);
				pixels[o + 1] = (byte)(y * 255 / height);
				pixels[o + 2] = 180;
				pixels[o + 3] = 255;
			}
		}
		return BitmapSource.FromRgba8(width, height, pixels);
	}
}
