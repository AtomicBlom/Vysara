using SUIsei.Drawing;

namespace SUIsei.Media;

/// <summary>
/// An image an <see cref="Controls.Image"/> or an <see cref="ImageBrush"/> draws, as WinUI's <c>ImageSource</c>: a
/// <see cref="Imaging.BitmapSource"/> (decoded texels that never change), a <see cref="Imaging.WriteableBitmap"/> (texels
/// the app rewrites), or a <see cref="Extensions.HostImageSource"/> (texels the host supplies). Each declares its
/// <see cref="TextureSource.AlphaMode"/> and <see cref="TextureSource.ColorSpace"/>, so straight-alpha and sRGB images
/// blend correctly.
/// <para>
/// Unlike WinUI's, an image source is itself the neutral <see cref="TextureSource"/> a draw list's slot holds: it has no
/// dependency properties of its own, so it is not a <c>DependencyObject</c>, and the composition layer needs no second
/// object for it.
/// </para>
/// <para>
/// <b>Lifetime.</b> A host keeps an image's texture while any of its surfaces draws the image, and is told to release it
/// once none does (<see cref="UIHost.ReleasedTextures"/>), including an image a hot-reload rebuild left behind.
/// <see cref="Dispose"/> (SUIsei-only, as WinUI's image sources are not disposable) lets it go at once: the image draws
/// nothing from then on, and the hosts that held it release its texture after their next render.
/// </para>
/// </summary>
public abstract class ImageSource : TextureSource, IDisposable
{
	private protected ImageSource(TextureAlphaMode alphaMode, TextureColorSpace colorSpace)
		: base(alphaMode, colorSpace)
	{
	}

	/// <summary>
	/// Stop drawing the image and release its texture now, rather than when nothing draws it any more. Every surface
	/// that drew it is redrawn without it at its host's next render, which reports it in
	/// <see cref="UIHost.ReleasedTextures"/>. Disposing twice does nothing.
	/// </summary>
	public void Dispose()
	{
		MarkDisposed();
		GC.SuppressFinalize(this);
	}
}
