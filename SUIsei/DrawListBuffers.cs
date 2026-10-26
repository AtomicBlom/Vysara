using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// One set of a surface's retained draw-list streams. A <see cref="UIDrawList"/> keeps two and fills them in turn,
/// so the list it built last stays readable while it builds the next. <see cref="Revision"/> names the composite that
/// last filled the set; a <see cref="DrawListData"/> bound to an older revision has had its contents overwritten.
/// </summary>
internal sealed class DrawListBuffers
{
	public DrawListBuffers() => ImagesView = Images.AsReadOnly();

	public RetainedBuffer<OverlayVertex> Vertices { get; } = new(256);

	public RetainedBuffer<ushort> Indices { get; } = new(384);

	public RetainedBuffer<DrawCommand> Commands { get; } = new(16);

	/// <summary>The images the list's <see cref="UIPrimKind.Image"/> commands sample, each once.</summary>
	public List<ImageSource> Images { get; } = [];

	/// <summary>A read-only view of <see cref="Images"/>, made once so handing it out allocates nothing.</summary>
	public IReadOnlyList<ImageSource> ImagesView { get; }

	/// <summary>The revision of the composite that last filled this set; 0 before the first.</summary>
	public uint Revision { get; private set; }

	/// <summary>Empty the set for the composite <paramref name="revision"/>, keeping its storage.</summary>
	public void Reset(uint revision)
	{
		Vertices.Clear();
		Indices.Clear();
		Commands.Clear();
		Images.Clear();
		Revision = revision;
	}
}
