using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei.Drawing;

/// <summary>
/// One frame's flattened draw list — a variable-size vertex stream, its 16-bit index stream, and the batched
/// draw commands that scissor + texture + index-range it. Backend-neutral (no graphics-API types), so the whole
/// producer pipeline up to GPU upload is CPU-testable against <see cref="Dump"/>. Rebuilt only when its source
/// changes; an unchanged frame re-submits the same Revision.
/// </summary>
public sealed class DrawListData
{
	/// <summary>An empty list — the no-op a clean or disabled producer hands the channel.</summary>
	public static DrawListData Empty { get; } = new();

	/// <summary>The vertex stream (target-pixel positions; premultiplied colour).</summary>
	public ReadOnlyMemory<OverlayVertex> Vertices { get; init; }

	/// <summary>The index stream (16-bit + VertexOffset for >64k vertices).</summary>
	public ReadOnlyMemory<ushort> Indices { get; init; }

	/// <summary>The batched draw commands, in submission order.</summary>
	public ReadOnlyMemory<DrawCommand> Commands { get; init; }

	/// <summary>The images referenced by <see cref="UIPrimKind.Image"/> commands this frame (by
	/// <see cref="ImageSource.TextureIndex"/>) — the engine glue uploads + binds each before submitting.</summary>
	public IReadOnlyList<ImageSource> Images { get; init; } = [];

	/// <summary>The pixel size of the coordinate space the geometry is authored in.</summary>
	public Vector2 CoordinateSize { get; init; }

	/// <summary>True when this list targets an HDR surface.</summary>
	public bool IsHdrTarget { get; init; }

	/// <summary>A monotonically increasing damage/no-op token: an unchanged list re-submits the same revision.</summary>
	public uint Revision { get; init; }

	/// <summary>
	/// The region (in surface pixels) that changed versus the previous revision — the union of the bounds of every
	/// vertex that differs, expanded to the whole surface on a structural change (commands added/removed/reordered),
	/// when group-opacity layers are present, or on the first build. Lets a host repaint only the dirty rectangle.
	/// The compositor stamps it; <see cref="Rect.IsEmpty"/> means nothing changed.
	/// </summary>
	public Rect Damage { get; internal set; }

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
}
