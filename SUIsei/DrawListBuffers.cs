using System.Numerics;
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

	/// <summary>Per slot, whether what it shows differs from what was shown before: from the previous list's slot
	/// (<see cref="CompareTextures"/>), or from this list's own recorded version (<see cref="RefreshVersions"/>).</summary>
	public List<bool> ChangedSlots { get; } = [];

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
		ChangedSlots.Clear();
		Revision = revision;
	}

	/// <summary>Whether this set's slot table names the same sources, in the same slots and at the same versions, as
	/// <paramref name="previous"/>'s. Records in <see cref="ChangedSlots"/> each slot whose source or version differs, or
	/// that the previous table lacks.</summary>
	public bool CompareTextures(DrawListBuffers previous)
	{
		var same = Slots.Count == previous.Slots.Count;
		ChangedSlots.Clear();
		for (var i = 0; i < Slots.Count; i++)
		{
			var changed = i >= previous.Slots.Count
				|| !ReferenceEquals(Slots[i], previous.Slots[i]) || SlotVersions[i] != previous.SlotVersions[i];
			ChangedSlots.Add(changed);
			same &= !changed;
		}
		return same;
	}

	/// <summary>Re-record each slot's current version; true when any had moved since it was recorded. Records in
	/// <see cref="ChangedSlots"/> the slots that moved.</summary>
	public bool RefreshVersions()
	{
		var moved = false;
		ChangedSlots.Clear();
		for (var i = 0; i < Slots.Count; i++)
		{
			var version = Slots[i].Version;
			var changed = version != SlotVersions[i];
			ChangedSlots.Add(changed);
			if (!changed)
				continue;
			SlotVersions[i] = version;
			moved = true;
		}
		return moved;
	}

	/// <summary>The surface pixels drawn by commands that sample a slot <see cref="ChangedSlots"/> marks, each command's
	/// vertex bounds cut to its clip: where texels changed under unchanged geometry. Empty when none did.</summary>
	public Rect TexelDamage()
	{
		var commands = Commands.Span;
		var vertices = Vertices.Span;
		var indices = Indices.Span;
		var damage = Rect.Zero;
		for (var c = 0; c < commands.Length; c++)
		{
			ref readonly var cmd = ref commands[c];
			if ((uint)cmd.TextureIndex >= (uint)ChangedSlots.Count || !ChangedSlots[cmd.TextureIndex] || cmd.IndexCount == 0)
				continue;
			var min = new Vector2(float.MaxValue);
			var max = new Vector2(float.MinValue);
			for (var i = cmd.IndexOffset; i < cmd.IndexOffset + cmd.IndexCount; i++)
			{
				var p = vertices[cmd.VertexOffset + indices[i]].Position;
				min = Vector2.Min(min, p);
				max = Vector2.Max(max, p);
			}
			damage = damage.Union(new Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y).Intersect(cmd.ClipRect));
		}
		return damage;
	}
}
