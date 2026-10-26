namespace SUIsei.Drawing;

/// <summary>
/// Texels a draw list samples through one of its texture slots: an <see cref="ImageSource"/>, a glyph atlas page
/// (<see cref="AtlasImage"/>), or the <see cref="White"/> texel. A host uploads each source it meets in a list's
/// <see cref="DrawListData.TextureSlots"/> and binds it by slot. A source is identified by reference, and
/// <see cref="Version"/> moves when its texels change, so a host replaces an upload only when it is out of date.
/// </summary>
public abstract class TextureSource
{
	private protected TextureSource()
	{
	}

	/// <summary>The 1×1 opaque white texel in every list's slot 0: what untextured commands (fills, borders)
	/// sample.</summary>
	public static TextureSource White { get; } = new WhiteTexel();

	public abstract int Width { get; }

	public abstract int Height { get; }

	/// <summary>The texels: RGBA8, row-major, <see cref="Width"/> × <see cref="Height"/> × 4 bytes.</summary>
	public abstract ReadOnlyMemory<byte> Pixels { get; }

	/// <summary>Moves each time the texels change. 0 for a source whose texels never change, which is every source
	/// SUIsei has today.</summary>
	public uint Version { get; private set; }

	/// <summary>Record that the texels changed: the next draw list that samples this source, or the surface's
	/// current one on its next frame, moves its <see cref="DrawListData.TextureRevision"/>.</summary>
	internal void InvalidateTexels() => Version++;

	private sealed class WhiteTexel : TextureSource
	{
		private static readonly byte[] Texel = [255, 255, 255, 255];

		public override int Width => 1;

		public override int Height => 1;

		public override ReadOnlyMemory<byte> Pixels => Texel;
	}
}
