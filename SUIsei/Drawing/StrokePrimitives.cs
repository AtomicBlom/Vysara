using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Drawing;

/// <summary>
/// The triangles a stroke is built from, each carrying the signed distances to the edges it antialiases. Shared by the
/// tessellator, which builds a whole stroke, and by trimming at emit, which rebuilds the pieces cut at the trim points,
/// so both draw identical geometry.
/// <para>
/// Every piece reaches <c>margin</c> (half a pixel) past the true outline, so the antialiasing ramp is rasterized on the
/// outside, and every distance is linear across its triangle. A triangle carries only the edges whose ramps it holds:
/// a segment's body its two sides and any cap clip, a join or cap one outer edge per triangle (joins and caps are split
/// on their bisectors so two nearly parallel edges never share a triangle, where the product of their ramps would
/// darken the edge).
/// </para>
/// </summary>
internal static class StrokePrimitives
{
	private const float Unused = VectorVertex.Unused;

	/// <summary>
	/// How far, in pixels, a body reaches under the join or cap it meets: several times what a GPU's vertex snapping moves
	/// a vertex at the 1/256 of a pixel D3D requires (Vulkan allows as coarse as 1/16, but desktop GPUs and Adreno snap to
	/// 1/256). Inside the stroke both pieces cover the overlap fully, so it shows nothing. Where it crosses the outer edge's
	/// antialiasing ramp, a pixel whose centre falls in it takes both pieces' coverage, up to a quarter more than its own,
	/// as the bodies' overlap on the inside of every turn already does; few centres fall in so thin a sliver, and an
	/// uncovered centre where pieces only abut is far worse.
	/// </summary>
	internal const float SeamOverlap = 1f / 32f;

	/// <summary>The left-hand normal of a unit direction.</summary>
	public static Vector2 Normal(Vector2 t) => new(-t.Y, t.X);

	/// <summary>
	/// A segment's body from <paramref name="x0"/> to <paramref name="x1"/> along it (measured from
	/// <paramref name="start"/>), as two triangles. The distances are its two sides, plus a start clip (a flat or square
	/// cap's end line, at <paramref name="startClip"/> along the segment) and an end clip, or unused.
	/// <para>
	/// An end without a clip meets a join or a round or triangle cap along its cross-section, and those pieces hold only
	/// half of that edge (their spokes start at the centreline), so the two would meet at a T-junction. A GPU snaps
	/// vertices to its sub-pixel grid, which can open a T-junction into a hairline that a pixel centre falls through. So
	/// such an end reaches <see cref="SeamOverlap"/> of a pixel past the cross-section, under the piece it meets, keeping
	/// the arc length of the end it extends so arc lengths stay within the stroke's. An end with a clip meets its cap
	/// extension along the very same edge, computed the same way, so it needs no overlap.
	/// </para>
	/// </summary>
	public static void Body(RetainedBuffer<VectorVertex> target, Vector2 start, Vector2 t, float x0, float x1, float h, float margin,
		float? startClip, float? endClip, float arcAtStart)
	{
		var n = Normal(t);
		var w = h + margin;
		var overlap = 2f * margin * SeamOverlap; // margin is half a pixel
		var e0 = startClip is null ? x0 - overlap : x0;
		var e1 = endClip is null ? x1 + overlap : x1;
		var a0 = start + e0 * t;
		var a1 = start + e1 * t;
		var p00 = a0 + w * n;
		var p01 = a0 - w * n;
		var p10 = a1 + w * n;
		var p11 = a1 - w * n;
		var g0 = arcAtStart + x0;
		var g1 = arcAtStart + x1;
		Triangle(target, V(p00, g0), V(p10, g1), V(p11, g1));
		Triangle(target, V(p00, g0), V(p11, g1), V(p01, g0));

		VectorVertex V(Vector2 p, float arc) => new(p, BodyDistances(p, start, t, n, h, startClip, endClip), arc);
	}

	/// <summary>
	/// The part of a flat or square cap past the end of the stroke: a quad from <paramref name="x0"/> to
	/// <paramref name="x1"/> along the segment, with the segment's sides and the cap's end line as its distances. The arc
	/// length is constant across it.
	/// </summary>
	public static void CapExtension(RetainedBuffer<VectorVertex> target, Vector2 start, Vector2 t, float x0, float x1, float h, float margin,
		float? startClip, float? endClip, float arc)
	{
		var n = Normal(t);
		var w = h + margin;
		var p00 = start + x0 * t + w * n;
		var p01 = start + x0 * t - w * n;
		var p10 = start + x1 * t + w * n;
		var p11 = start + x1 * t - w * n;
		Triangle(target, V(p00), V(p10), V(p11));
		Triangle(target, V(p00), V(p11), V(p01));

		VectorVertex V(Vector2 p) => new(p, BodyDistances(p, start, t, n, h, startClip, endClip), arc);
	}

	private static Vector4 BodyDistances(Vector2 p, Vector2 start, Vector2 t, Vector2 n, float h, float? startClip, float? endClip)
	{
		var d = p - start;
		var u = Vector2.Dot(d, n);
		var along = Vector2.Dot(d, t);
		return new Vector4(h - u, h + u, startClip is { } s ? along - s : Unused, endClip is { } e ? e - along : Unused);
	}

	/// <summary>
	/// A cap at <paramref name="point"/>, where the stroke runs along <paramref name="t"/> and the cap faces
	/// <paramref name="outward"/> (<c>-t</c> at a start, <c>t</c> at an end). Flat and square caps are drawn as body
	/// clips and extensions by the caller; this draws round and triangle caps, which stand alone past the end line.
	/// </summary>
	public static void Cap(RetainedBuffer<VectorVertex> target, PenLineCap cap, Vector2 point, Vector2 t, Vector2 outward, float h, float margin,
		float tolerance, float arc)
	{
		var n = Normal(t);
		switch (cap)
		{
			case PenLineCap.Round:
				{
					// Half a circle from one side, round the outward direction, to the other.
					var from = MathF.Atan2(n.Y, n.X);
					var sweep = Vector2.Dot(Normal(n), outward) > 0 ? MathF.PI : -MathF.PI;
					Fan(target, point, from, sweep, h, margin, tolerance, arc);
					break;
				}
			case PenLineCap.Triangle:
				{
					// Two halves split on the axis, each antialiasing its slanted edge, which runs from the side at half a
					// thickness to the apex half a thickness out.
					var reach = h + MathF.Sqrt(2f) * margin;
					TriangleCapHalf(target, point, n, outward, h, reach, arc);
					TriangleCapHalf(target, point, -n, outward, h, reach, arc);
					break;
				}
		}
	}

	private static void TriangleCapHalf(RetainedBuffer<VectorVertex> target, Vector2 point, Vector2 side, Vector2 outward, float h, float reach, float arc)
	{
		var edgeNormal = Vector2.Normalize(side + outward);
		var inset = h / MathF.Sqrt(2f);
		Triangle(target, V(point), V(point + reach * side), V(point + reach * outward));

		VectorVertex V(Vector2 p) => new(p, new Vector4(inset - Vector2.Dot(p - point, edgeNormal), Unused, Unused, Unused), arc);
	}

	/// <summary>
	/// The outer side of a corner at <paramref name="point"/>, where the stroke turns from <paramref name="t1"/> to
	/// <paramref name="t2"/>. The inner side needs nothing: the two segments' bodies overlap there. A straight
	/// continuation draws nothing.
	/// </summary>
	public static void Join(RetainedBuffer<VectorVertex> target, PenLineJoin join, Vector2 point, Vector2 t1, Vector2 t2, float h, float margin,
		float miterLimit, float tolerance, float arc)
	{
		var cross = t1.X * t2.Y - t1.Y * t2.X;
		var dot = Vector2.Dot(t1, t2);
		if (MathF.Abs(cross) < 1e-6f && dot > 0)
			return;

		// The outer side is away from the turn.
		var o1 = cross > 0 ? -Normal(t1) : Normal(t1);
		var o2 = cross > 0 ? -Normal(t2) : Normal(t2);
		var sum = o1 + o2;
		var bisector = sum.LengthSquared() > 1e-12f ? Vector2.Normalize(sum) : t1;
		var cosHalf = MathF.Max(Vector2.Dot(o1, bisector), 0f);   // cos of half the turn
		var sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - cosHalf * cosHalf));
		var w = h + margin;

		switch (join)
		{
			case PenLineJoin.Round:
				{
					var from = MathF.Atan2(o1.Y, o1.X);
					var sweep = MathF.Acos(Math.Clamp(Vector2.Dot(o1, o2), -1f, 1f)) * (cross > 0 ? 1f : -1f);
					Fan(target, point, from, sweep, h, margin, tolerance, arc);
					break;
				}
			case PenLineJoin.Bevel:
				{
					// The bevel line is at h·cos(θ/2) from the corner along the bisector; each half antialiases it.
					var lineAt = h * cosHalf;
					var rayReach = cosHalf > 0.05f ? (lineAt + margin) / cosHalf : 4f * w;
					var tip = point + (lineAt + margin) * bisector;
					var p1 = point + rayReach * o1;
					var p2 = point + rayReach * o2;
					Triangle(target, V(point), V(p1), V(tip));
					Triangle(target, V(point), V(tip), V(p2));

					VectorVertex V(Vector2 p) => new(p, new Vector4(lineAt - Vector2.Dot(p - point, bisector), Unused, Unused, Unused), arc);
					break;
				}
			default:
				{
					var limit = MathF.Max(miterLimit, 1f);
					var p1 = point + w * o1;
					var p2 = point + w * o2;
					if (cosHalf * limit >= 1f)
					{
						// Within the limit: the outer edges meet at the miter point. Split on the bisector, each half
						// antialiases its own segment's side.
						var tip = point + w / cosHalf * bisector;
						Triangle(target, Side(point, o1), Side(p1, o1), Side(tip, o1));
						Triangle(target, Side(point, o2), Side(tip, o2), Side(p2, o2));
					}
					else
					{
						// Cut off at limit·h along the bisector: each half is a quad between its side and the cut.
						var cut = limit * h;
						var reach = (cut + margin - w * cosHalf) / MathF.Max(sinHalf, 1e-6f);
						var x1 = p1 + reach * t1;
						var x2 = p2 - reach * t2;
						var c = point + (cut + margin) * bisector;
						Triangle(target, Clipped(point, o1), Clipped(p1, o1), Clipped(x1, o1));
						Triangle(target, Clipped(point, o1), Clipped(x1, o1), Clipped(c, o1));
						Triangle(target, Clipped(point, o2), Clipped(c, o2), Clipped(x2, o2));
						Triangle(target, Clipped(point, o2), Clipped(x2, o2), Clipped(p2, o2));

						VectorVertex Clipped(Vector2 p, Vector2 side) =>
							new(p, new Vector4(h - Vector2.Dot(p - point, side), cut - Vector2.Dot(p - point, bisector), Unused, Unused), arc);
					}

					VectorVertex Side(Vector2 p, Vector2 side) => new(p, new Vector4(h - Vector2.Dot(p - point, side), Unused, Unused, Unused), arc);
					break;
				}
		}
	}

	/// <summary>
	/// A round fan about <paramref name="center"/> of radius <paramref name="h"/>, from angle <paramref name="from"/>
	/// through <paramref name="sweep"/> radians. Its outline is a polygon of tangent lines, each within
	/// <paramref name="tolerance"/> of the circle; each wedge antialiases its own tangent, so every distance is linear.
	/// </summary>
	public static void Fan(RetainedBuffer<VectorVertex> target, Vector2 center, float from, float sweep, float h, float margin, float tolerance, float arc)
	{
		if (MathF.Abs(sweep) < 1e-6f || h <= 0)
			return;
		// A tangent polygon strays at most h·(1/cos(δ/2) - 1) from its circle; keep that within the tolerance.
		var maxStep = 2f * MathF.Acos(Math.Clamp(h / (h + tolerance), 0f, 1f));
		maxStep = Math.Clamp(maxStep, 0.02f, MathF.PI / 3f);
		var steps = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / maxStep), 1, 512);
		var step = sweep / steps;
		var reach = (h + margin) / MathF.Cos(step / 2f);
		// Each rim point is computed once and shared by the wedges either side of it: recomputed per wedge, the two
		// copies round apart and leave a hairline gap along the spoke that a pixel centre can fall into.
		var rim = center + reach * Dir(from);
		for (var i = 0; i < steps; i++)
		{
			var next = center + reach * Dir(from + (i + 1) * step);
			var mid = Dir(from + (i + 0.5f) * step);
			Triangle(target, V(center), V(rim), V(next));
			rim = next;

			VectorVertex V(Vector2 p) => new(p, new Vector4(h - Vector2.Dot(p - center, mid), Unused, Unused, Unused), arc);
		}
	}

	private static Vector2 Dir(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

	public static void Triangle(RetainedBuffer<VectorVertex> target, in VectorVertex a, in VectorVertex b, in VectorVertex c)
	{
		target.Add(a);
		target.Add(b);
		target.Add(c);
	}
}
