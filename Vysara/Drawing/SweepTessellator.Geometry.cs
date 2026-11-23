/*
** SGI FREE SOFTWARE LICENSE B (Version 2.0, Sept. 18, 2008)
** Copyright (C) [dates of first publication] Silicon Graphics, Inc.
** All Rights Reserved.
**
** Permission is hereby granted, free of charge, to any person obtaining a copy
** of this software and associated documentation files (the "Software"), to deal
** in the Software without restriction, including without limitation the rights
** to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
** of the Software, and to permit persons to whom the Software is furnished to do so,
** subject to the following conditions:
**
** The above copyright notice including the dates of first publication and either this
** permission notice or a reference to http://oss.sgi.com/projects/FreeB/ shall be
** included in all copies or substantial portions of the Software.
**
** THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
** INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
** PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL SILICON GRAPHICS, INC.
** BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
** TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
** OR OTHER DEALINGS IN THE SOFTWARE.
**
** Except as contained in this notice, the name of Silicon Graphics, Inc. shall not
** be used in advertising or otherwise to promote the sale, use or other dealings in
** this Software without prior written authorization from Silicon Graphics, Inc.
*/
/*
** Author: Eric Veach, July 1994. Ported to C# for Vysara from Mikko Mononen's libtess2.
*/

namespace Vysara.Drawing;

internal sealed partial class SweepTessellator
{
	private static bool VertEq(Vertex u, Vertex v) => u.S == v.S && u.T == v.T;

	// Whether u is lexicographically <= v: the order the sweep line meets vertices in.
	private static bool VertLeq(Vertex u, Vertex v) => u.S < v.S || (u.S == v.S && u.T <= v.T);

	// VertLeq with s and t transposed.
	private static bool TransLeq(Vertex u, Vertex v) => u.T < v.T || (u.T == v.T && u.S <= v.S);

	private static bool EdgeGoesLeft(HalfEdge e) => VertLeq(e.Dst!, e.Org!);

	private static bool EdgeGoesRight(HalfEdge e) => VertLeq(e.Org!, e.Dst!);

	/// <summary>
	/// Given u, v, w with VertLeq(u,v) and VertLeq(v,w), the signed distance from the edge uw to v: v.t minus uw at v.s.
	/// Zero for a vertical uw. The calculation is accurate and stable even when v is very close to u or w; with v.t = 0
	/// the negated result lies between u.t and w.t.
	/// </summary>
	private static double EdgeEval(Vertex u, Vertex v, Vertex w) => EdgeEval(u, v.S, v.T, w);

	private static double EdgeEval(Vertex u, double vs, double vt, Vertex w)
	{
		var gapL = vs - u.S;
		var gapR = w.S - vs;
		if (gapL + gapR > 0)
		{
			return gapL < gapR
				? (vt - u.T) + (u.T - w.T) * (gapL / (gapL + gapR))
				: (vt - w.T) + (w.T - u.T) * (gapR / (gapL + gapR));
		}
		return 0; // vertical line
	}

	// libtess2 evaluates EdgeSign with EdgeEval: the cheaper form's sign disagrees when x is close to 0 (libtess2 issue 22).
	private static double EdgeSign(Vertex u, Vertex v, Vertex w) => EdgeEval(u, v, w);

	// EdgeEval with s and t transposed.
	private static double TransEval(Vertex u, Vertex v, Vertex w)
	{
		var gapL = v.T - u.T;
		var gapR = w.T - v.T;
		if (gapL + gapR > 0)
		{
			return gapL < gapR
				? (v.S - u.S) + (u.S - w.S) * (gapL / (gapL + gapR))
				: (v.S - w.S) + (w.S - u.S) * (gapR / (gapL + gapR));
		}
		return 0;
	}

	private static double TransSign(Vertex u, Vertex v, Vertex w)
	{
		var gapL = v.T - u.T;
		var gapR = w.T - v.T;
		return gapL + gapR > 0 ? (v.S - w.S) * gapL + (v.S - u.S) * gapR : 0;
	}

	/// <summary>
	/// (b·x + a·y) / (a + b), or (x + y) / 2 when a == b == 0, with a and b clamped to 0 or more. The result always lies
	/// between x and y, the only property the sweep relies on, and is accurate even when a and b differ greatly.
	/// </summary>
	private static double Interpolate(double a, double x, double b, double y)
	{
		a = a < 0 ? 0 : a;
		b = b < 0 ? 0 : b;
		return a <= b
			? b == 0 ? x / 2 + y / 2 : x + (y - x) * (a / (a + b))
			: y + (x - y) * (b / (a + b));
	}

	/// <summary>
	/// The intersection of edges (o1,d1) and (o2,d2), guaranteed to lie in the intersection of their bounding rectangles:
	/// the two middle vertices in the VertLeq order give s by interpolation, then the same in the TransLeq order gives t.
	/// </summary>
	private static void EdgeIntersect(Vertex o1, Vertex d1, Vertex o2, Vertex d2, out double s, out double t)
	{
		double z1, z2;

		if (!VertLeq(o1, d1)) (o1, d1) = (d1, o1);
		if (!VertLeq(o2, d2)) (o2, d2) = (d2, o2);
		if (!VertLeq(o1, o2))
		{
			(o1, o2) = (o2, o1);
			(d1, d2) = (d2, d1);
		}

		if (!VertLeq(o2, d1))
		{
			// Technically, no intersection -- do our best
			s = o2.S / 2 + d1.S / 2;
		}
		else if (VertLeq(d1, d2))
		{
			// Interpolate between o2 and d1
			z1 = EdgeEval(o1, o2, d1);
			z2 = EdgeEval(o2, d1, d2);
			if (z1 + z2 < 0)
			{
				z1 = -z1;
				z2 = -z2;
			}
			s = Interpolate(z1, o2.S, z2, d1.S);
		}
		else
		{
			// Interpolate between o2 and d2
			z1 = EdgeSign(o1, o2, d1);
			z2 = -EdgeSign(o1, d2, d1);
			if (z1 + z2 < 0)
			{
				z1 = -z1;
				z2 = -z2;
			}
			s = Interpolate(z1, o2.S, z2, d2.S);
		}

		// Now repeat the process for t
		if (!TransLeq(o1, d1)) (o1, d1) = (d1, o1);
		if (!TransLeq(o2, d2)) (o2, d2) = (d2, o2);
		if (!TransLeq(o1, o2))
		{
			(o1, o2) = (o2, o1);
			(d1, d2) = (d2, d1);
		}

		if (!TransLeq(o2, d1))
			t = o2.T / 2 + d1.T / 2;
		else if (TransLeq(d1, d2))
		{
			z1 = TransEval(o1, o2, d1);
			z2 = TransEval(o2, d1, d2);
			if (z1 + z2 < 0)
			{
				z1 = -z1;
				z2 = -z2;
			}
			t = Interpolate(z1, o2.T, z2, d1.T);
		}
		else
		{
			z1 = TransSign(o1, o2, d1);
			z2 = -TransSign(o1, d2, d1);
			if (z1 + z2 < 0)
			{
				z1 = -z1;
				z2 = -z2;
			}
			t = Interpolate(z1, o2.T, z2, d2.T);
		}
	}
}
