using System.Globalization;
using System.Numerics;
using System.Text;
using SUIsei.Drawing;

namespace SUIsei.Composition;

/// <summary>
/// A vector shape: a <see cref="CompositionPath"/> filled and stroked, as WinUI's <c>CompositionSpriteShape</c> draws its
/// geometry. The path is drawn through <see cref="PathTransform"/> (a geometry's transform, a shape's stretch), which maps
/// the outline but not the pen. The fill and the stroke are each painted by a <see cref="CompositionBrush"/>: a colour, a
/// linear gradient or an image, measured over <see cref="Bounds"/>. The stroke follows <see cref="StrokeStyle"/>, and
/// draws the part <see cref="Trim"/> leaves.
/// <para>
/// Retained and versioned like the rest of the composition content. The path, the stroke style and the brushes are
/// retained objects versioned on their own, so a change made inside one of them leaves this content's
/// <see cref="VisualContent.Version"/> alone; a compositor keying work on what the shape draws reads their versions too.
/// </para>
/// <para>
/// The tessellations are cached here, keyed by the path (which object, and its version), the path transform, the stroke
/// style's version and the scale bucket the shape is drawn at, so moving, fading, recolouring or trimming a shape never
/// re-tessellates it, and the cache needs no lookup and is collected with the content. A foreign compositor reads the
/// path, transform, paint and style and draws them natively, or takes the tessellation SUIsei's compositor draws from
/// <see cref="TessellateFill"/> and <see cref="TessellateStroke"/>.
/// </para>
/// </summary>
public sealed class ShapeContent : VisualContent
{
	// Scratch for the tessellations: content belongs to its UI thread, and a thread draws one shape at a time.
	[ThreadStatic] private static Tessellator? _tessellator;

	private readonly TessellatedPath _fill = new();
	private readonly TessellatedPath _stroke = new();
	private Rect _bounds;
	private (CompositionPath? Path, uint Version, Matrix3x2 Transform) _boundsSource;

	/// <summary>The outline, or null for none. A path is retained and versioned on its own: change it in place, and a
	/// geometry's own path may be shown here as it is.</summary>
	public CompositionPath? Path { get; set => Change(ref field, value); }

	/// <summary>Maps the path's points into the visual's local space before it is filled or stroked: a geometry's
	/// transform, a shape's stretch. The pen is not mapped, so the stroke keeps its thickness. Identity by default.</summary>
	public Matrix3x2 PathTransform { get; set => Change(ref field, value); } = Matrix3x2.Identity;

	/// <summary>The fill's paint, or null for no fill. It fills the path's filled figures by its fill rule.</summary>
	public CompositionBrush? Fill { get; set => Change(ref field, value); }

	/// <summary>The stroke's paint, or null for no stroke.</summary>
	public CompositionBrush? Stroke { get; set => Change(ref field, value); }

	/// <summary>How the stroke is drawn: thickness, joins, caps and dashes. Retained and versioned on its own; set its
	/// properties in place.</summary>
	public CompositionStrokeStyle StrokeStyle { get; } = new();

	/// <summary>The part of the stroke drawn, as WinUI's <c>CompositionGeometry.TrimStart</c>, <c>TrimEnd</c> and
	/// <c>TrimOffset</c>; the whole stroke by default. Applied when the stroke is drawn, so changing it never
	/// re-tessellates. A fill is not trimmed.</summary>
	public PathTrim Trim { get; set => Change(ref field, value); } = PathTrim.None;

	/// <summary>
	/// Whether a gradient <see cref="Stroke"/> runs along the stroke rather than across space: its offsets measure the
	/// outline's untrimmed length, 0 at its start and 1 at its end, all its figures in order, and its start and end points
	/// are not used. Exact, as every vertex of a stroke carries its arc length. False by default.
	/// </summary>
	public bool StrokeBrushAlongLength { get; set => Change(ref field, value); }

	/// <summary>
	/// The tight bounds of the path through <see cref="PathTransform"/>, in the visual's local space, or
	/// <see cref="Rect.Zero"/> without a path: the box the brushes are measured over, as WinUI measures a shape's brushes
	/// over its geometry's bounds. Recomputed only when the path or the transform changes.
	/// </summary>
	public Rect Bounds
	{
		get
		{
			if (Path is not { } path)
				return Rect.Zero;
			var source = (path, path.Version, PathTransform);
			if (_boundsSource != source)
			{
				_bounds = path.GetBounds(PathTransform);
				_boundsSource = source;
			}
			return _bounds;
		}
	}

	/// <summary>
	/// The fill tessellated for drawing at <paramref name="scale"/>, in the visual's local space: the cached one when the
	/// path, its transform and the scale bucket are unchanged, otherwise re-tessellated in place. Null without a path. The
	/// result is retained by this content; read it before the shape changes.
	/// </summary>
	public TessellatedPath? TessellateFill(TessellationScale scale)
	{
		if (Path is not { } path)
			return null;
		if (!_fill.IsCurrentFor(path, PathTransform, null, scale))
			(_tessellator ??= new Tessellator()).Fill(path, PathTransform, scale, _fill);
		return _fill;
	}

	/// <summary>
	/// The stroke tessellated for drawing at <paramref name="scale"/>, untrimmed (a trim applies when it is drawn): the
	/// cached one when the path, its transform, the stroke style and the scale bucket are unchanged, otherwise
	/// re-tessellated in place. Null without a path. The result is retained by this content.
	/// </summary>
	public TessellatedPath? TessellateStroke(TessellationScale scale)
	{
		if (Path is not { } path)
			return null;
		if (!_stroke.IsCurrentFor(path, PathTransform, StrokeStyle, scale))
			(_tessellator ??= new Tessellator()).Stroke(path, PathTransform, StrokeStyle, scale, _stroke);
		return _stroke;
	}

	/// <summary>
	/// Whether <paramref name="point"/>, in the visual's local space, is on what the shape draws at
	/// <paramref name="scale"/>: inside the fill when there is a fill, or on the stroke when there is one, within half its
	/// thickness of the outline along the dashes, caps and joins it draws and only where the trim leaves it. A point counts
	/// where its coverage is at least half, past the middle of each antialiased edge. Uses the cached tessellations, and
	/// allocates nothing.
	/// </summary>
	public bool HitTest(Vector2 point, TessellationScale scale)
	{
		if (Path is not { IsEmpty: false })
			return false;
		if (Fill is not null && Covers(TessellateFill(scale)!, PathTrim.None, point))
			return true;
		return Stroke is not null && StrokeStyle.Thickness > 0f && Covers(TessellateStroke(scale)!, Trim, point);
	}

	private static bool Covers(TessellatedPath path, PathTrim trim, Vector2 point)
	{
		var bounds = path.Bounds;
		if (point.X < bounds.X || point.Y < bounds.Y || point.X > bounds.Right || point.Y > bounds.Bottom)
			return false;
		var probe = new CoverageProbe(point);
		path.Emit(ref probe, trim);
		return probe.Hit;
	}

	/// <summary>Finds whether any triangle drawn covers a point: the point inside it, and every edge distance interpolated
	/// there at least 0.</summary>
	private struct CoverageProbe(Vector2 point) : IVectorSink
	{
		public bool Hit;

		public void AddTriangles(ReadOnlySpan<VectorVertex> vertices)
		{
			for (var i = 0; !Hit && i + 2 < vertices.Length; i += 3)
			{
				Vector2 a = vertices[i].Position, b = vertices[i + 1].Position, c = vertices[i + 2].Position;
				var area = Cross(b - a, c - a);
				if (area == 0f)
					continue;
				var wa = Cross(b - point, c - point) / area;
				var wb = Cross(c - point, a - point) / area;
				var wc = 1f - wa - wb;
				if (wa < 0f || wb < 0f || wc < 0f)
					continue;
				var d = wa * vertices[i].Distances + wb * vertices[i + 1].Distances + wc * vertices[i + 2].Distances;
				Hit = d.X >= 0f && d.Y >= 0f && d.Z >= 0f && d.W >= 0f;
			}
		}

		private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
	}

	internal override void Describe(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(" shape");
		if (Path is { } path)
			sb.Append(c, $" path=\"{path}\"");
		var m = PathTransform;
		if (!m.IsIdentity)
			sb.Append(c, $" xform=[{m.M11:0.###},{m.M12:0.###},{m.M21:0.###},{m.M22:0.###},{m.M31:0.###},{m.M32:0.###}]");
		if (Fill is { } fill)
		{
			sb.Append(" fill=");
			fill.Describe(sb);
		}
		var style = StrokeStyle;
		if (Stroke is { } stroke && style.Thickness > 0)
		{
			sb.Append(c, $" stroke={style.Thickness:0.###}");
			stroke.Describe(sb);
			if (StrokeBrushAlongLength)
				sb.Append(" along-length");
			if (style.LineJoin != CompositionStrokeLineJoin.Miter)
				sb.Append(c, $" join={style.LineJoin}");
			else if (style.MiterLimit != 10f)
				sb.Append(c, $" miter={style.MiterLimit:0.###}");
			if (style.StartCap != CompositionStrokeCap.Flat || style.EndCap != CompositionStrokeCap.Flat)
				sb.Append(c, $" caps={style.StartCap},{style.EndCap}");
			if (style.IsDashed)
			{
				sb.Append(" dashes=");
				var dashes = style.DashArray;
				for (var i = 0; i < dashes.Length; i++)
					sb.Append(c, $"{(i > 0 ? "," : "")}{dashes[i]:0.###}");
				if (style.DashOffset != 0f)
					sb.Append(c, $"@{style.DashOffset:0.###}");
				if (style.DashCap != CompositionStrokeCap.Flat)
					sb.Append(c, $" dashcap={style.DashCap}");
				if (_stroke.DashesLimited)
					sb.Append(" (past the dash limit, the rest drawn solid)");
			}
			if (!Trim.IsNone)
				sb.Append(c, $" trim={Trim.Start:0.###}..{Trim.End:0.###}+{Trim.Offset:0.###}");
		}
	}
}
