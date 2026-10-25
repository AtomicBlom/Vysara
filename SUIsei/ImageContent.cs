using System.Globalization;
using System.Text;

using SUIsei.Drawing;

namespace SUIsei;

/// <summary>A tinted image drawn into <see cref="Dest"/> (element-local pixels) from <see cref="Source"/>
/// — the <see cref="Controls.Image"/> primitive the compositor emits as a <see cref="UIPrimKind.Image"/> command.</summary>
public sealed class ImageContent(ImageSource source, Rect dest) : VisualContent
{
	/// <summary>The image to draw.</summary>
	public ImageSource Source { get; set => Change(ref field, value); } = source;

	/// <summary>Where the whole image lands, in the visual's local space.</summary>
	public Rect Dest { get; set => Change(ref field, value); } = dest;

	/// <summary>Set everything this content draws at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void Update(ImageSource source, Rect dest)
	{
		Source = source;
		Dest = dest;
	}

	internal override void Describe(StringBuilder sb) =>
		sb.Append(CultureInfo.InvariantCulture,
			$" image={Source.Width}x{Source.Height} dest=({Dest.X:0.###},{Dest.Y:0.###} {Dest.Width:0.###}x{Dest.Height:0.###})");
}
