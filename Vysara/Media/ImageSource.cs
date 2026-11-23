using Vysara.Composition;
using Vysara.Drawing;

namespace Vysara.Media;

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
/// <see cref="Dispose"/> (Vysara-only, as WinUI's image sources are not disposable) lets it go without waiting for
/// nothing to draw it: each host that draws the image redraws without it at its next render and releases its texture
/// then. An image keeps no reference to the hosts that draw it, so a long-lived one keeps none of them alive.
/// </para>
/// </summary>
public abstract class ImageSource : TextureSource, IDisposable
{
	private protected ImageSource(TextureAlphaMode alphaMode, TextureColorSpace colorSpace)
		: base(alphaMode, colorSpace)
	{
	}

	/// <summary>
	/// Stop drawing the image and release its texture at the next render, rather than when nothing draws it any more.
	/// Each host whose surfaces draw it redraws them without it at its next render, which reports it in
	/// <see cref="UIHost.ReleasedTextures"/>; until then a list it already handed out may still name the image, and is
	/// drawn as it was. A foreign <see cref="IUICompositor"/> reads <see cref="TextureSource.IsDisposed"/> itself. Safe on
	/// any thread; disposing twice does nothing.
	/// </summary>
	public void Dispose()
	{
		MarkDisposed();
		GC.SuppressFinalize(this);
	}
}
