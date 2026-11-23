using System.Text;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Extensions;

/// <summary>
/// An image whose texels the host supplies, named by <see cref="Tag"/>: a render target, a video frame, anything the
/// host draws itself. Vysara-only (WinUI has no such source) and code-only. It has a size, so it lays out and is placed by
/// a brush's stretch like any image, but no texels: <see cref="TextureSource.Pixels"/> is empty, and a host binds its own
/// texture for the tag to the source's slot (Kaldra: <c>UIPainter.SetExternalTexture</c>), reading it as
/// <see cref="TextureSource.AlphaMode"/> and <see cref="TextureSource.ColorSpace"/> declare. A host that has nothing
/// for the tag fails loud. The CPU rasterizer skips what samples it and counts the commands it skipped.
/// <para>
/// The host owns the texture, so a released or disposed host image tells the host to stop binding it, not to destroy
/// it. Its size is fixed: to show a resized target, make a new source with the same tag.
/// </para>
/// </summary>
public sealed class HostImageSource : ImageSource
{
	/// <param name="tag">The name the host binds the texture under.</param>
	/// <param name="width">The texture's width in texels, which the image lays out and stretches by.</param>
	/// <param name="height">The texture's height in texels.</param>
	/// <param name="alphaMode">Whether the host's texels are premultiplied; a render target blended premultiplied is.</param>
	/// <param name="colorSpace">How the host's texels decode to linear light; the host binds a view that decodes them so.</param>
	public HostImageSource(string tag, int width, int height,
		TextureAlphaMode alphaMode = TextureAlphaMode.Premultiplied, TextureColorSpace colorSpace = TextureColorSpace.Linear)
		: base(alphaMode, colorSpace)
	{
		ArgumentException.ThrowIfNullOrEmpty(tag);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		Tag = tag;
		Width = width;
		Height = height;
	}

	/// <summary>The name the host binds this image's texture under.</summary>
	public string Tag { get; }

	public override int Width { get; }

	public override int Height { get; }

	/// <summary>Always empty: the host supplies the texels.</summary>
	public override ReadOnlyMemory<byte> Pixels => ReadOnlyMemory<byte>.Empty;

	private protected override void DescribeIdentity(StringBuilder sb) => sb.Append(" \"").Append(Tag).Append('"');
}
