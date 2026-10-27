using System.Globalization;
using System.Text;

namespace SUIsei.Drawing;

/// <summary>
/// Texels a draw list samples through one of its texture slots: an image (<see cref="Media.ImageSource"/>), a glyph atlas
/// page (<see cref="AtlasImage"/>), or the <see cref="White"/> texel. A host uploads each source it meets in a list's
/// <see cref="DrawListData.TextureSlots"/> and binds it by slot. A source is identified by reference, and
/// <see cref="Version"/> moves when its texels change, so a host replaces an upload only when it is out of date.
/// <para>
/// A source declares how to read its texels: <see cref="ColorSpace"/> says how the bytes decode to linear light (the
/// texture format a host binds it through), and <see cref="AlphaMode"/> whether the decoded colour is premultiplied. Both
/// are fixed for the source's life and belong to the slot, not to the commands that sample it, so every kind that samples
/// a slot (an image quad, a textured vector triangle) reads it the same way.
/// </para>
/// </summary>
public abstract class TextureSource
{
	// The hosts that hold this source resident (usually one), so disposing it can reach them. Made on first use.
	private List<TextureResidency>? _holders;

	private protected TextureSource(TextureAlphaMode alphaMode, TextureColorSpace colorSpace)
	{
		AlphaMode = alphaMode;
		ColorSpace = colorSpace;
	}

	/// <summary>The 1×1 opaque white texel in every list's slot 0: what untextured commands (fills, borders)
	/// sample.</summary>
	public static TextureSource White { get; } = new WhiteTexel();

	public abstract int Width { get; }

	public abstract int Height { get; }

	/// <summary>The texels: RGBA8, row-major, <see cref="Width"/> × <see cref="Height"/> × 4 bytes. Empty for a source
	/// whose texels the host supplies itself (<see cref="Extensions.HostImageSource"/>).</summary>
	public abstract ReadOnlyMemory<byte> Pixels { get; }

	/// <summary>Whether the decoded colour is premultiplied by alpha or straight.</summary>
	public TextureAlphaMode AlphaMode { get; }

	/// <summary>How the RGB bytes decode to linear light: sRGB (decoded per texel, before filtering) or linear.</summary>
	public TextureColorSpace ColorSpace { get; }

	/// <summary>Moves each time the texels change (<see cref="Media.Imaging.WriteableBitmap.Invalidate"/>). A host
	/// holding an upload of an older version replaces it: it uploads the new texels and releases the old texture.</summary>
	public uint Version { get; private set; }

	/// <summary>True once the source has been disposed: it draws nothing, and a host that held it was told to release
	/// it (<see cref="UIHost.ReleasedTextures"/>).</summary>
	public bool IsDisposed { get; private set; }

	/// <summary>Record that the texels changed: the next draw list that samples this source, or the surface's
	/// current one on its next frame, moves its <see cref="DrawListData.TextureRevision"/>.</summary>
	internal void InvalidateTexels() => Version++;

	/// <summary>Mark the source disposed and tell every host holding it, which reports it for release and redraws the
	/// surfaces that drew it.</summary>
	private protected void MarkDisposed()
	{
		if (IsDisposed)
			return;
		IsDisposed = true;
		if (_holders is not { } holders)
			return;
		for (var i = holders.Count - 1; i >= 0; i--)
			holders[i].Disposed(this);
		holders.Clear();
	}

	internal void AddHolder(TextureResidency residency) => (_holders ??= []).Add(residency);

	internal void RemoveHolder(TextureResidency residency) => _holders?.Remove(residency);

	/// <summary>One line describing the source for a dump: its kind, size, version and how its texels read.</summary>
	internal void Describe(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(ReferenceEquals(this, White) ? "White" : GetType().Name);
		DescribeIdentity(sb);
		sb.Append(c, $" {Width}x{Height} v{Version}");
		sb.Append(AlphaMode == TextureAlphaMode.Straight ? " straight" : " premultiplied");
		sb.Append(ColorSpace == TextureColorSpace.Srgb ? " srgb" : " linear");
		if (IsDisposed)
			sb.Append(" disposed");
	}

	/// <summary>What names this source beyond its kind in a dump, such as a host image's tag.</summary>
	private protected virtual void DescribeIdentity(StringBuilder sb)
	{
	}

	private sealed class WhiteTexel() : TextureSource(TextureAlphaMode.Premultiplied, TextureColorSpace.Linear)
	{
		private static readonly byte[] Texel = [255, 255, 255, 255];

		public override int Width => 1;

		public override int Height => 1;

		public override ReadOnlyMemory<byte> Pixels => Texel;
	}
}
