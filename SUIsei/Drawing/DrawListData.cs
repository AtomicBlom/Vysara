using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei.Drawing;

/// <summary>
/// One frame's flattened draw list — a variable-size vertex stream, its 16-bit index stream, and the batched
/// draw commands that scissor + texture + index-range it. Backend-neutral (no graphics-API types), so the whole
/// producer pipeline up to GPU upload is CPU-testable against <see cref="Dump"/>. Rebuilt only when its source
/// changes; an unchanged frame re-submits the same Revision.
/// <para>
/// A list a <see cref="DrawListCompositor"/> produced is a view over buffers the compositor keeps and refills. It
/// stays readable through the next composite of its surface, which fills a second set of buffers, and its streams
/// (<see cref="Vertices"/>, <see cref="Indices"/>, <see cref="Commands"/>, <see cref="Images"/>, <see cref="Dump"/>)
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
	private IReadOnlyList<ImageSource> _images = [];
	private Vector2 _coordinateSize;
	private bool _isHdrTarget;
	private uint _revision;

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

	/// <summary>The images referenced by <see cref="UIPrimKind.Image"/> commands this frame (by
	/// <see cref="ImageSource.TextureIndex"/>) — the engine glue uploads + binds each before submitting.</summary>
	public IReadOnlyList<ImageSource> Images
	{
		get
		{
			ThrowIfStale();
			return _images;
		}
		init => _images = value;
	}

	/// <summary>The pixel size of the coordinate space the geometry is authored in.</summary>
	public Vector2 CoordinateSize { get => _coordinateSize; init => _coordinateSize = value; }

	/// <summary>True when this list targets an HDR surface.</summary>
	public bool IsHdrTarget { get => _isHdrTarget; init => _isHdrTarget = value; }

	/// <summary>A monotonically increasing damage/no-op token: an unchanged list re-submits the same revision.</summary>
	public uint Revision { get => _revision; init => _revision = value; }

	/// <summary>
	/// The region (in surface pixels) that changed versus the previous revision — the union of the bounds of every
	/// vertex that differs, expanded to the whole surface on a structural change (commands added/removed/reordered),
	/// when group-opacity layers are present, or on the first build. Lets a host repaint only the dirty rectangle.
	/// The compositor stamps it; <see cref="Rect.IsEmpty"/> means nothing changed.
	/// </summary>
	public Rect Damage { get; internal set; }

	/// <summary>
	/// Point this list at <paramref name="source"/>'s current contents, as the list of the composite that filled it.
	/// From then on it reads that set's live arrays and goes stale when the set is refilled. <see cref="Damage"/> is
	/// cleared for the compositor to stamp.
	/// </summary>
	internal void Bind(DrawListBuffers source, Vector2 coordinateSize, bool isHdrTarget)
	{
		_source = source;
		_revision = source.Revision;
		_vertices = source.Vertices.Memory;
		_indices = source.Indices.Memory;
		_commands = source.Commands.Memory;
		_images = source.ImagesView;
		_coordinateSize = coordinateSize;
		_isHdrTarget = isHdrTarget;
		Damage = default;
	}

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

		var sb = new StringBuilder();
		sb.Append(c, $"DrawListData (rev {Revision}, coords {CoordinateSize.X:0.###}x{CoordinateSize.Y:0.###}, hdr={IsHdrTarget})\n");

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
