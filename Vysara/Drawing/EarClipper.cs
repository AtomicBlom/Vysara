/*
** ISC License
**
** Copyright (c) 2016, Mapbox
**
** Permission to use, copy, modify, and/or distribute this software for any purpose
** with or without fee is hereby granted, provided that the above copyright notice
** and this permission notice appear in all copies.
**
** THE SOFTWARE IS PROVIDED "AS IS" AND ISC DISCLAIMS ALL WARRANTIES WITH REGARD TO
** THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS.
** IN NO EVENT SHALL ISC BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR
** CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA
** OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
** ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
**
** This file follows the algorithm of mapbox/earcut (https://github.com/mapbox/earcut).
*/

using System.Numerics;

namespace Vysara.Drawing;

/// <summary>
/// Triangulates a simple polygon with holes by ear clipping, after the algorithm of mapbox's <c>earcut</c> (ISC
/// licence, © Mapbox): each hole is bridged into the outer ring at a vertex visible from its leftmost point, then ears
/// (convex vertices whose triangle holds no other vertex) are clipped until one triangle is left. Degenerate input
/// falls back to removing collinear points, curing local self-crossings and splitting along a valid diagonal, so it
/// always terminates.
/// <para>
/// The outer ring has a positive signed area (<c>Σ x_i y_(i+1) - x_(i+1) y_i</c>) and every hole a negative one. Nodes
/// live in a retained pool, so triangulating polygons of a steady size allocates nothing.
/// </para>
/// </summary>
internal sealed class EarClipper
{
	private struct Node
	{
		public int Index;   // into the caller's points
		public float X, Y;
		public int Prev, Next;
	}

	private readonly RetainedBuffer<Node> _nodes = new(64);
	private readonly RetainedBuffer<int> _holes = new(4);
	private RetainedBuffer<int> _triangles = null!;

	/// <summary>
	/// Triangulate the outer ring <c>points[outer]</c> with the holes <c>points[holes[i]]</c>, appending each triangle's
	/// three point indices to <paramref name="triangles"/>.
	/// </summary>
	public void Triangulate(ReadOnlySpan<Vector2> points, (int Start, int Count) outer, ReadOnlySpan<(int Start, int Count)> holes,
		RetainedBuffer<int> triangles)
	{
		_nodes.Clear();
		_triangles = triangles;
		var outerNode = Ring(points, outer.Start, outer.Count);
		if (outerNode < 0 || N(outerNode).Next == N(outerNode).Prev)
			return;
		if (holes.Length > 0)
			outerNode = EliminateHoles(points, holes, outerNode);
		EarcutLinked(outerNode, 0);
	}

	private ref Node N(int i) => ref _nodes[i];

	// Link a ring in its given order; returns its last node, or -1 for an empty ring.
	private int Ring(ReadOnlySpan<Vector2> points, int start, int count)
	{
		var last = -1;
		for (var i = start; i < start + count; i++)
			last = Insert(i, points[i], last);
		if (last >= 0 && Equal(last, N(last).Next))
		{
			Remove(last);
			last = N(last).Next;
		}
		return last;
	}

	private int Insert(int index, Vector2 p, int last)
	{
		var node = _nodes.Count;
		_nodes.Add(new Node { Index = index, X = p.X, Y = p.Y, Prev = node, Next = node });
		if (last >= 0)
		{
			ref var n = ref N(node);
			n.Next = N(last).Next;
			n.Prev = last;
			N(N(last).Next).Prev = node;
			N(last).Next = node;
		}
		return node;
	}

	private void Remove(int p)
	{
		N(N(p).Next).Prev = N(p).Prev;
		N(N(p).Prev).Next = N(p).Next;
	}

	private bool Equal(int a, int b) => N(a).X == N(b).X && N(a).Y == N(b).Y;

	// Twice the signed area of the triangle p, q, r in earcut's convention: negative for a convex turn of a ring with a
	// positive area.
	private float Area(int p, int q, int r)
	{
		ref var a = ref N(p);
		ref var b = ref N(q);
		ref var c = ref N(r);
		return (b.Y - a.Y) * (c.X - b.X) - (b.X - a.X) * (c.Y - b.Y);
	}

	private void EarcutLinked(int ear, int pass)
	{
		if (ear < 0)
			return;
		var stop = ear;
		while (N(ear).Prev != N(ear).Next)
		{
			var prev = N(ear).Prev;
			var next = N(ear).Next;
			if (IsEar(ear))
			{
				Emit(prev, ear, next);
				Remove(ear);
				ear = N(next).Next;
				stop = N(next).Next;
				continue;
			}
			ear = next;
			if (ear != stop)
				continue;
			// No ear in a whole lap: tidy the ring and try again, then cure local crossings, then split it.
			switch (pass)
			{
				case 0:
					EarcutLinked(FilterPoints(ear, -1), 1);
					break;
				case 1:
					EarcutLinked(CureLocalIntersections(FilterPoints(ear, -1)), 2);
					break;
				default:
					SplitEarcut(ear);
					break;
			}
			break;
		}
	}

	private void Emit(int a, int b, int c)
	{
		_triangles.Add(N(a).Index);
		_triangles.Add(N(b).Index);
		_triangles.Add(N(c).Index);
	}

	private bool IsEar(int ear)
	{
		int a = N(ear).Prev, b = ear, c = N(ear).Next;
		if (Area(a, b, c) >= 0)
			return false; // reflex
		ref var na = ref N(a);
		ref var nb = ref N(b);
		ref var nc = ref N(c);
		float x0 = MathF.Min(na.X, MathF.Min(nb.X, nc.X)), y0 = MathF.Min(na.Y, MathF.Min(nb.Y, nc.Y));
		float x1 = MathF.Max(na.X, MathF.Max(nb.X, nc.X)), y1 = MathF.Max(na.Y, MathF.Max(nb.Y, nc.Y));
		var p = N(c).Next;
		while (p != a)
		{
			ref var np = ref N(p);
			if (np.X >= x0 && np.X <= x1 && np.Y >= y0 && np.Y <= y1
				&& !(na.X == np.X && na.Y == np.Y)
				&& PointInTriangle(na.X, na.Y, nb.X, nb.Y, nc.X, nc.Y, np.X, np.Y)
				&& Area(np.Prev, p, np.Next) >= 0)
				return false;
			p = np.Next;
		}
		return true;
	}

	private static bool PointInTriangle(float ax, float ay, float bx, float by, float cx, float cy, float px, float py) =>
		(cx - px) * (ay - py) >= (ax - px) * (cy - py)
		&& (ax - px) * (by - py) >= (bx - px) * (ay - py)
		&& (bx - px) * (cy - py) >= (cx - px) * (by - py);

	// Remove duplicate and collinear points from start round to end (start's previous lap when end is -1).
	private int FilterPoints(int start, int end)
	{
		if (start < 0)
			return start;
		if (end < 0)
			end = start;
		var p = start;
		bool again;
		do
		{
			again = false;
			if (Equal(p, N(p).Next) || Area(N(p).Prev, p, N(p).Next) == 0)
			{
				Remove(p);
				p = end = N(p).Prev;
				if (p == N(p).Next)
					break;
				again = true;
			}
			else
				p = N(p).Next;
		} while (again || p != end);
		return end;
	}

	private int CureLocalIntersections(int start)
	{
		var p = start;
		do
		{
			int a = N(p).Prev, b = N(N(p).Next).Next;
			if (!Equal(a, b) && Intersects(a, p, N(p).Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
			{
				Emit(a, p, b);
				Remove(p);
				Remove(N(p).Next);
				p = start = b;
			}
			p = N(p).Next;
		} while (p != start);
		return FilterPoints(p, -1);
	}

	private void SplitEarcut(int start)
	{
		var a = start;
		do
		{
			var b = N(N(a).Next).Next;
			while (b != N(a).Prev)
			{
				if (N(a).Index != N(b).Index && IsValidDiagonal(a, b))
				{
					var c = SplitPolygon(a, b);
					a = FilterPoints(a, N(a).Next);
					c = FilterPoints(c, N(c).Next);
					EarcutLinked(a, 0);
					EarcutLinked(c, 0);
					return;
				}
				b = N(b).Next;
			}
			a = N(a).Next;
		} while (a != start);
	}

	private int EliminateHoles(ReadOnlySpan<Vector2> points, ReadOnlySpan<(int Start, int Count)> holes, int outerNode)
	{
		_holes.Clear();
		foreach (var (start, count) in holes)
		{
			var list = Ring(points, start, count);
			if (list >= 0)
				_holes.Add(Leftmost(list));
		}
		// Bridge the holes in order of their leftmost x, so each bridge sees the holes already merged.
		for (var i = 1; i < _holes.Count; i++)
		{
			for (var j = i; j > 0 && N(_holes[j]).X < N(_holes[j - 1]).X; j--)
				(_holes[j], _holes[j - 1]) = (_holes[j - 1], _holes[j]);
		}
		for (var i = 0; i < _holes.Count; i++)
			outerNode = EliminateHole(_holes[i], outerNode);
		return outerNode;
	}

	private int EliminateHole(int hole, int outerNode)
	{
		var bridge = FindHoleBridge(hole, outerNode);
		if (bridge < 0)
			return outerNode;
		var bridgeReverse = SplitPolygon(bridge, hole);
		FilterPoints(bridgeReverse, N(bridgeReverse).Next);
		return FilterPoints(bridge, N(bridge).Next);
	}

	// A vertex of the outer ring visible from the hole's leftmost point: cast a ray left to the nearest edge, then pick
	// the reflex vertex inside the triangle it spans with the smallest angle, if any.
	private int FindHoleBridge(int hole, int outerNode)
	{
		var p = outerNode;
		float hx = N(hole).X, hy = N(hole).Y;
		var qx = float.NegativeInfinity;
		var m = -1;
		do
		{
			ref var np = ref N(p);
			ref var nn = ref N(np.Next);
			if (hy <= np.Y && hy >= nn.Y && nn.Y != np.Y)
			{
				var x = np.X + (hy - np.Y) * (nn.X - np.X) / (nn.Y - np.Y);
				if (x <= hx && x > qx)
				{
					qx = x;
					m = np.X < nn.X ? p : np.Next;
					if (x == hx)
						return m; // the hole touches this edge
				}
			}
			p = np.Next;
		} while (p != outerNode);
		if (m < 0)
			return -1;

		var stop = m;
		float mx = N(m).X, my = N(m).Y;
		var tanMin = float.PositiveInfinity;
		p = m;
		do
		{
			ref var np = ref N(p);
			if (hx >= np.X && np.X >= mx && hx != np.X
				&& PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, np.X, np.Y))
			{
				var tan = MathF.Abs(hy - np.Y) / (hx - np.X);
				if (LocallyInside(p, hole)
					&& (tan < tanMin || (tan == tanMin && (np.X > N(m).X || (np.X == N(m).X && SectorContainsSector(m, p))))))
				{
					m = p;
					tanMin = tan;
				}
			}
			p = np.Next;
		} while (p != stop);
		return m;
	}

	private bool SectorContainsSector(int m, int p) => Area(N(m).Prev, m, N(p).Prev) < 0 && Area(N(p).Next, m, N(m).Next) < 0;

	private int Leftmost(int start)
	{
		int p = start, leftmost = start;
		do
		{
			if (N(p).X < N(leftmost).X || (N(p).X == N(leftmost).X && N(p).Y < N(leftmost).Y))
				leftmost = p;
			p = N(p).Next;
		} while (p != start);
		return leftmost;
	}

	// Join a to b with a diagonal, splitting the ring in two; returns the copy of b on the new ring.
	private int SplitPolygon(int a, int b)
	{
		var a2 = _nodes.Count;
		_nodes.Add(new Node { Index = N(a).Index, X = N(a).X, Y = N(a).Y });
		var b2 = _nodes.Count;
		_nodes.Add(new Node { Index = N(b).Index, X = N(b).X, Y = N(b).Y });
		int an = N(a).Next, bp = N(b).Prev;
		N(a).Next = b;
		N(b).Prev = a;
		N(a2).Next = an;
		N(an).Prev = a2;
		N(b2).Next = a2;
		N(a2).Prev = b2;
		N(bp).Next = b2;
		N(b2).Prev = bp;
		return b2;
	}

	private bool IsValidDiagonal(int a, int b) =>
		N(N(a).Next).Index != N(b).Index && N(N(a).Prev).Index != N(b).Index && !IntersectsPolygon(a, b)
		&& ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) && (Area(N(a).Prev, a, N(b).Prev) != 0 || Area(a, N(b).Prev, b) != 0))
			|| (Equal(a, b) && Area(N(a).Prev, a, N(a).Next) > 0 && Area(N(b).Prev, b, N(b).Next) > 0));

	private bool Intersects(int p1, int q1, int p2, int q2)
	{
		var o1 = Math.Sign(Area(p1, q1, p2));
		var o2 = Math.Sign(Area(p1, q1, q2));
		var o3 = Math.Sign(Area(p2, q2, p1));
		var o4 = Math.Sign(Area(p2, q2, q1));
		if (o1 != o2 && o3 != o4)
			return true;
		return (o1 == 0 && OnSegment(p1, p2, q1)) || (o2 == 0 && OnSegment(p1, q2, q1))
			|| (o3 == 0 && OnSegment(p2, p1, q2)) || (o4 == 0 && OnSegment(p2, q1, q2));
	}

	private bool OnSegment(int p, int q, int r) =>
		N(q).X <= MathF.Max(N(p).X, N(r).X) && N(q).X >= MathF.Min(N(p).X, N(r).X)
		&& N(q).Y <= MathF.Max(N(p).Y, N(r).Y) && N(q).Y >= MathF.Min(N(p).Y, N(r).Y);

	private bool IntersectsPolygon(int a, int b)
	{
		var p = a;
		do
		{
			var next = N(p).Next;
			if (N(p).Index != N(a).Index && N(next).Index != N(a).Index && N(p).Index != N(b).Index && N(next).Index != N(b).Index
				&& Intersects(p, next, a, b))
				return true;
			p = next;
		} while (p != a);
		return false;
	}

	private bool LocallyInside(int a, int b) =>
		Area(N(a).Prev, a, N(a).Next) < 0
			? Area(a, b, N(a).Next) >= 0 && Area(a, N(a).Prev, b) >= 0
			: Area(a, b, N(a).Prev) < 0 || Area(a, N(a).Next, b) < 0;

	private bool MiddleInside(int a, int b)
	{
		var p = a;
		var inside = false;
		float px = (N(a).X + N(b).X) / 2, py = (N(a).Y + N(b).Y) / 2;
		do
		{
			ref var np = ref N(p);
			ref var nn = ref N(np.Next);
			if ((np.Y > py) != (nn.Y > py) && nn.Y != np.Y && px < (nn.X - np.X) * (py - np.Y) / (nn.Y - np.Y) + np.X)
				inside = !inside;
			p = np.Next;
		} while (p != a);
		return inside;
	}
}
