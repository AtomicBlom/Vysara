using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using SUIsei.Composition;

namespace SUIsei.Drawing;

/// <summary>
/// One frame's flattened draw list — a variable-size vertex stream, its 16-bit index stream, and the batched
/// draw commands that scissor + texture + index-range it. Backend-neutral (no graphics-API types), so the whole
/// producer pipeline up to GPU upload is CPU-testable against <see cref="Dump"/>. Rebuilt only when its source
/// changes; an unchanged frame re-submits the same Revision.
/// <para>
/// A list a <see cref="DrawListCompositor"/> produced is a view over buffers the compositor keeps and refills. It
/// stays readable through the next composite of its surface, which fills a second set of buffers, and its streams
/// (<see cref="Vertices"/>, <see cref="Indices"/>, <see cref="Commands"/>, <see cref="TextureSlots"/>, <see cref="Dump"/>)
/// throw <see cref="InvalidOperationException"/> once a later composite refills its own set. Memory and spans taken
/// from it share that lifetime but are not checked. The compositor also reuses the view objects, so a reference kept
/// for four or more composites can be rebound to a newer list: read the surface's current list each frame rather
/// than keeping one.
/// </para>
/// </summary>
public sealed class DrawListData
{
	// The compositor buffer set this list is a view over, or null for a list that owns its streams.
	private DrawListBuffers? _source;
	private ReadOnlyMemory<OverlayVertex> _vertices;
	private ReadOnlyMemory<ushort> _indices;
	private ReadOnlyMemory<DrawCommand> _commands;
	private IReadOnlyList<TextureSource> _textureSlots = [];
	private uint _textureRevision;
	private Vector2 _coordinateSize;
	private bool _isHdrTarget;
	private uint _revision;
	private Rect _damage;

	/// <summary>An empty list — the no-op a clean or disabled producer hands the channel.</summary>
	public static DrawListData Empty { get; } = new();

	/// <summary>The vertex stream (target-pixel positions; premultiplied colour).</summary>
	public ReadOnlyMemory<OverlayVertex> Vertices
	{
		get
		{
			ThrowIfStale();
			return _vertices;
		}
		init => _vertices = value;
	}

	/// <summary>
	/// The index stream. Indices are 16-bit and relative to their command's <see cref="DrawCommand.VertexOffset"/>,
	/// so a list can hold more vertices than one 16-bit index can address.
	/// </summary>
	public ReadOnlyMemory<ushort> Indices
	{
		get
		{
			ThrowIfStale();
			return _indices;
		}
		init => _indices = value;
	}

	/// <summary>The batched draw commands, in submission order.</summary>
	public ReadOnlyMemory<DrawCommand> Commands
	{
		get
		{
			ThrowIfStale();
			return _commands;
		}
		init => _commands = value;
	}

	/// <summary>
	/// The list's texture slot table: a command's <see cref="DrawCommand.TextureIndex"/> is an index into it. Slot 0
	/// is always <see cref="TextureSource.White"/>, which untextured commands sample; the others are the sources the
	/// list's glyph and image commands sample (glyph atlas pages, images), each once, numbered densely in the order the
	/// list first uses them. The host uploads each source and binds them by slot; slots belong to this list only. Every
	/// command's <see cref="DrawCommand.TextureIndex"/> is below <c>TextureSlots.Count</c>, so a host can treat one
	/// outside the table as a broken list.
	/// <para>
	/// How a slot is sampled is its source's: a host binds an <see cref="TextureColorSpace.Srgb"/> source through an sRGB
	/// format (decoded per texel, before filtering) and a linear one through a plain format, and premultiplies what an
	/// image or vector command samples from a <see cref="TextureAlphaMode.Straight"/> source after the decode and the
	/// filtering. A command carries only its slot, so every kind that samples it reads it the same way.
	/// </para>
	/// </summary>
	public IReadOnlyList<TextureSource> TextureSlots
	{
		get
		{
			ThrowIfStale();
			return _textureSlots;
		}
		init => _textureSlots = value;
	}

	/// <summary>
	/// Moves when <see cref="TextureSlots"/> names different sources, puts them in different slots, or one of them
	/// changes its texels (<see cref="TextureSource.Version"/>), including on a frame that keeps the same
	/// <see cref="Revision"/>. A host rebinds and re-uploads textures only when it moves; a geometry-only change
	/// leaves it alone.
	/// </summary>
	public uint TextureRevision { get => _textureRevision; init => _textureRevision = value; }

	/// <summary>The pixel size of the coordinate space the geometry is authored in.</summary>
	public Vector2 CoordinateSize { get => _coordinateSize; init => _coordinateSize = value; }

	/// <summary>True when this list targets an HDR surface.</summary>
	public bool IsHdrTarget { get => _isHdrTarget; init => _isHdrTarget = value; }

	/// <summary>A monotonically increasing damage/no-op token: an unchanged list re-submits the same revision.</summary>
	public uint Revision { get => _revision; init => _revision = value; }

	/// <summary>
	/// The region (in surface pixels) that changed since the list this surface handed out before — the union of the bounds
	/// of every vertex that differs, expanded to the whole surface on a structural change (commands added/removed/reordered),
	/// when group-opacity layers are present, or on the first build. Lets a host repaint only the dirty rectangle.
	/// The compositor stamps it each time it hands the list out; <see cref="Rect.IsEmpty"/> means nothing changed. The same
	/// list handed out again unchanged has empty damage, unless a source it samples has changed its texels, when it covers the
	/// pixels drawn with that source (the whole surface when the list has group layers). A list from a
	/// <see cref="DrawListBuilder"/> damages the whole surface unless its build says otherwise. A foreign
	/// <see cref="IDrawListSource"/> sets it on the lists it makes.
	/// <para>
	/// A <see cref="UIHost.Render"/> that throws may leave its lists undrawn, so the damage they carried is carried again by
	/// the lists of the next render, unioned with their own. A host that skips drawing the lists of a render that returned
	/// repaints the whole of each surface the next time it draws: those lists' damage is measured from the ones it skipped.
	/// Comparing <see cref="Revision"/> with the last list drawn does not show the skip, because a list handed out again
	/// keeps its revision while its damage becomes empty.
	/// </para>
	/// </summary>
	public Rect Damage { get => _damage; init => _damage = value; }

	/// <summary>Stamp the damage of the list the compositor is handing out.</summary>
	internal void StampDamage(Rect damage) => _damage = damage;

	/// <summary>A copy of this list with <paramref name="damage"/> in place of its own, reading the same streams and going
	/// stale with them, so a list its producer still hands out is left as it is.</summary>
	internal DrawListData WithDamage(Rect damage)
	{
		var copy = (DrawListData)MemberwiseClone();
		copy._damage = damage;
		return copy;
	}

	/// <summary>
	/// Point this list at <paramref name="source"/>'s current contents, as the list of the composite that filled it.
	/// From then on it reads that set's live arrays and goes stale when the set is refilled. <see cref="Damage"/> is
	/// cleared for the compositor to stamp.
	/// </summary>
	internal void Bind(DrawListBuffers source, Vector2 coordinateSize, bool isHdrTarget, uint textureRevision)
	{
		_source = source;
		_revision = source.Revision;
		_vertices = source.Vertices.Memory;
		_indices = source.Indices.Memory;
		_commands = source.Commands.Memory;
		_textureSlots = source.SlotsView;
		_coordinateSize = coordinateSize;
		_isHdrTarget = isHdrTarget;
		_textureRevision = textureRevision;
		_damage = default;
	}

	/// <summary>Move the texture revision of the list the compositor is re-submitting unchanged, when a source it
	/// samples has changed its texels.</summary>
	internal void StampTextureRevision(uint textureRevision) => _textureRevision = textureRevision;

	/// <summary>
	/// A deterministic, human-readable text snapshot of the whole draw list — the "golden draw-list" a producer's
	/// CPU tests assert against. Stable across platforms (invariant-culture formatting).
	/// </summary>
	public string Dump()
	{
		var c = CultureInfo.InvariantCulture;
		var vertices = Vertices.Span;
		var indices = Indices.Span;
		var commands = Commands.Span;
		var slots = TextureSlots;

		var sb = new StringBuilder();
		sb.Append(c, $"DrawListData (rev {Revision}, texrev {TextureRevision}, coords {CoordinateSize.X:0.###}x{CoordinateSize.Y:0.###}, hdr={IsHdrTarget})\n");

		sb.Append(c, $"  Textures ({slots.Count}):\n");
		for (var i = 0; i < slots.Count; i++)
		{
			sb.Append(c, $"    [{i}] ");
			slots[i].Describe(sb);
			sb.Append('\n');
		}

		sb.Append(c, $"  Vertices ({vertices.Length}):\n");
		for (var i = 0; i < vertices.Length; i++)
		{
			ref readonly var v = ref vertices[i];
			sb.Append(c, $"    [{i}] pos=({v.Position.X:0.###},{v.Position.Y:0.###}) uv=({v.Uv.X:0.###},{v.Uv.Y:0.###})");
			sb.Append(c, $" col=({(float)v.Color.R:0.###},{(float)v.Color.G:0.###},{(float)v.Color.B:0.###},{(float)v.Color.A:0.###})");
			sb.Append(c, $" custom=({v.Custom.X:0.###},{v.Custom.Y:0.###},{v.Custom.Z:0.###},{v.Custom.W:0.###})\n");
		}

		sb.Append(c, $"  Indices ({indices.Length}):");
		for (var i = 0; i < indices.Length; i++)
			sb.Append(c, $" {indices[i]}");
		sb.Append('\n');

		sb.Append(c, $"  Commands ({commands.Length}):\n");
		for (var i = 0; i < commands.Length; i++)
		{
			ref readonly var cmd = ref commands[i];
			var r = cmd.ClipRect;
			var kind = (UIPrimKind)cmd.Tag;
			sb.Append(c, $"    [{i}] tag={cmd.Tag}({kind}) tex={cmd.TextureIndex} clip=({r.X:0.###},{r.Y:0.###} {r.Width:0.###}x{r.Height:0.###})");
			sb.Append(c, $" idx={cmd.IndexOffset}+{cmd.IndexCount} vtx={cmd.VertexOffset}\n");
		}

		return sb.ToString();
	}

	private void ThrowIfStale()
	{
		if (_source is not null && _source.Revision != _revision)
			ThrowStale(_revision, _source.Revision);
	}

	[DoesNotReturn]
	private static void ThrowStale(uint revision, uint current) => throw new InvalidOperationException(
		$"This draw list (revision {revision}) was read after composite {current} of its surface reused its buffers. " +
		"Read the surface's current draw list each frame instead of keeping an old one.");
}
