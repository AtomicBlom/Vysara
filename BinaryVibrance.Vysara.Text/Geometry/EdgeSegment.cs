namespace BinaryVibrance.Vysara.Text.Geometry;

/// <summary>
/// One outline segment of a glyph contour — a line, quadratic, or cubic Bézier — carrying its msdfgen edge colour.
/// The signed-distance math (closest point, sign from the cross product, the orthogonality tiebreak, and the
/// endpoint pseudo-distance correction) is a faithful port of msdfgen so the generated field matches the reference.
/// </summary>
internal abstract class EdgeSegment
{
	public EdgeColor Color = EdgeColor.White;

	/// <summary>The point on the segment at parameter <paramref name="t"/> ∈ [0,1].</summary>
	public abstract Vector2D Point(double t);

	/// <summary>The (un-normalized) tangent at <paramref name="t"/>.</summary>
	public abstract Vector2D Direction(double t);

	/// <summary>
	/// The signed distance from <paramref name="origin"/> to this segment, with the parameter of the closest point
	/// (which may fall outside [0,1] — see <see cref="DistanceToPseudoDistance"/>).
	/// </summary>
	public abstract SignedDistance SignedDistanceTo(Vector2D origin, out double param);

	/// <summary>Split into three equal-parameter sub-segments (used by the single-corner "teardrop" colouring case).</summary>
	public abstract void SplitInThirds(out EdgeSegment a, out EdgeSegment b, out EdgeSegment c);

	/// <summary>This edge traversed the other way, its colour kept.</summary>
	public abstract EdgeSegment Reversed();

	/// <summary>Expand the axis-aligned ink box to include this segment (endpoints + interior extrema).</summary>
	public abstract void ExtendBounds(ref double left, ref double bottom, ref double right, ref double top);

	/// <summary>
	/// Add this segment's contribution to the nonzero winding number of a point: count crossings of the rightward
	/// horizontal ray at <paramref name="y"/> that lie strictly right of <paramref name="xOrigin"/>, signed by
	/// whether the contour is moving up (+1) or down (−1) through the ray. A crossing at a vertex is counted on one side of
	/// it only (see <see cref="AddMonotoneCrossings"/>), so the count is exact for any ray. It is the exact fill the
	/// generator's polarity vote and error correction check the field against.
	/// </summary>
	public abstract void AddRayCrossings(double y, double xOrigin, ref int winding);

	/// <summary>
	/// Convert the closest-point signed distance into a pseudo-distance when the closest point is past an endpoint:
	/// substitute the perpendicular distance to the edge's extended line, removing the discontinuity at joins.
	/// </summary>
	public void DistanceToPseudoDistance(ref SignedDistance distance, Vector2D origin, double param)
	{
		if (param < 0)
		{
			var dir = Direction(0).Normalize();
			var aq = origin - Point(0);
			if (Vector2D.Dot(aq, dir) < 0)
			{
				var pseudo = Vector2D.Cross(aq, dir);
				if (Math.Abs(pseudo) <= Math.Abs(distance.Distance))
					distance = new SignedDistance(pseudo, 0);
			}
		}
		else if (param > 1)
		{
			var dir = Direction(1).Normalize();
			var bq = origin - Point(1);
			if (Vector2D.Dot(bq, dir) > 0)
			{
				var pseudo = Vector2D.Cross(bq, dir);
				if (Math.Abs(pseudo) <= Math.Abs(distance.Distance))
					distance = new SignedDistance(pseudo, 0);
			}
		}
	}

	protected static int Sign(double n) => n > 0 ? 1 : n < 0 ? -1 : 0;

	/// <summary>
	/// Add the crossings of a curve whose y turns at <paramref name="turns"/> (its y-extrema strictly inside (0,1), in
	/// increasing order), from <paramref name="start"/> to <paramref name="end"/>. Each piece between turns is monotone in
	/// y and counts the ray once when <paramref name="y"/> lies in the half-open span from its lower end to its upper
	/// end, the rule <see cref="LinearSegment"/> uses. So a ray through a vertex two segments share, or through a turning
	/// point, counts once where the outline crosses it and not at all where it only touches it. TrueType coordinates are
	/// whole font units and texel centres often land on them, so counting a shared vertex at both its segments would
	/// mark whole texel rows inside a counter as filled.
	/// </summary>
	protected void AddMonotoneCrossings(ReadOnlySpan<double> turns, Vector2D start, Vector2D end, double y, double xOrigin, ref int winding)
	{
		var ta = 0.0;
		var ya = start.Y;
		for (var i = 0; i <= turns.Length; i++)
		{
			var last = i == turns.Length;
			var tb = last ? 1.0 : turns[i];
			var yb = last ? end.Y : Point(tb).Y;
			if (((y >= ya && y < yb) || (y >= yb && y < ya)) && Point(CrossingParameter(ta, tb, yb > ya, y)).X > xOrigin)
				winding += Sign(yb - ya);

			ta = tb;
			ya = yb;
		}
	}

	// The parameter in [ta, tb] where a y-monotone piece meets y, by bisection: exact enough for an inside test, and free of
	// the root-selection ambiguity a closed-form solve has near the piece's ends.
	private double CrossingParameter(double ta, double tb, bool rising, double y)
	{
		for (var i = 0; i < 48; i++)
		{
			var mid = 0.5 * (ta + tb);
			var my = Point(mid).Y;
			if (rising ? my < y : my > y)
				ta = mid;
			else
				tb = mid;
		}

		return 0.5 * (ta + tb);
	}
}
