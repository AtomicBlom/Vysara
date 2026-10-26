using SUIsei.Drawing;

namespace SUIsei;

/// <summary>
/// One set of a surface's retained draw-list streams. A <see cref="UIDrawList"/> keeps two and fills them in turn,
/// so the list it built last stays readable while it builds the next. <see cref="Revision"/> names the composite that
/// last filled the set; a <see cref="DrawListData"/> bound to an older revision has had its contents overwritten.
/// </summary>
internal sealed class DrawListBuffers
{
	public DrawListBuffers() => SlotsView = Slots.AsReadOnly();

	public RetainedBuffer<OverlayVertex> Vertices { get; } = new(256);

	public RetainedBuffer<ushort> Indices { get; } = new(384);

	public RetainedBuffer<DrawCommand> Commands { get; } = new(16);

	/// <summary>The list's texture slot table: the source each slot holds, indexed by a command's
	/// <see cref="DrawCommand.TextureIndex"/>. Slot 0 is <see cref="TextureSource.White"/>.</summary>
	public List<TextureSource> Slots { get; } = [];

	/// <summary>Each slot's <see cref="TextureSource.Version"/> as this list recorded it, to tell when a source's
	/// texels have changed since.</summary>
	public List<uint> SlotVersions { get; } = [];

	/// <summary>A read-only view of <see cref="Slots"/>, made once so handing it out allocates nothing.</summary>
	public IReadOnlyList<TextureSource> SlotsView { get; }

	/// <summary>The revision of the composite that last filled this set; 0 before the first.</summary>
	public uint Revision { get; private set; }

	/// <summary>Empty the set for the composite <paramref name="revision"/>, keeping its storage.</summary>
	public void Reset(uint revision)
	{
		Vertices.Clear();
		Indices.Clear();
		Commands.Clear();
		Slots.Clear();
		SlotVersions.Clear();
		Revision = revision;
	}

	/// <summary>Whether this set's slot table names the same sources, in the same slots and at the same versions, as
	/// <paramref name="other"/>'s.</summary>
	public bool SameTextures(DrawListBuffers other)
	{
		if (Slots.Count != other.Slots.Count)
			return false;
		for (var i = 0; i < Slots.Count; i++)
		{
			if (!ReferenceEquals(Slots[i], other.Slots[i]) || SlotVersions[i] != other.SlotVersions[i])
				return false;
		}
		return true;
	}

	/// <summary>Re-record each slot's current version; true when any had moved since it was recorded.</summary>
	public bool RefreshVersions()
	{
		var moved = false;
		for (var i = 0; i < Slots.Count; i++)
		{
			var version = Slots[i].Version;
			if (version == SlotVersions[i])
				continue;
			SlotVersions[i] = version;
			moved = true;
		}
		return moved;
	}
}
