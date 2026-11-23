using Vysara.Extensions;
using Vysara.Media;
using Vysara.Media.Imaging;

namespace Vysara.Tests.Sample;

/// <summary>
/// The hand-authored half of the <c>Sample/ImageSourcesView.xaml</c> code-behind. Vysara.SourceGen emits the other half.
/// </summary>
internal partial class ImageSourcesView
{
	/// <summary>A new opaque red bitmap each time the tree is built, as a view that decodes its images when it builds
	/// does: a hot-reload rebuild leaves the old one behind.</summary>
	public ImageSource StillSource => BitmapSource.FromRgba8(1, 1, [255, 0, 0, 255]);

	/// <summary>One bitmap the view keeps and rewrites, shown by an image and an ellipse's brush.</summary>
	public WriteableBitmap LiveBitmap { get; } = new(2, 2);

	/// <summary>A texture the host supplies, named "minimap".</summary>
	public HostImageSource MinimapSource { get; } = new("minimap", 32, 16);
}
