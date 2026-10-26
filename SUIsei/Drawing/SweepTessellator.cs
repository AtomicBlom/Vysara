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
** Author: Eric Veach, July 1994. Ported to C# for SUIsei from Mikko Mononen's libtess2.
*/

using System.Numerics;

namespace SUIsei.Drawing;

/// <summary>
/// The GLU sweep-line tessellator (Eric Veach, SGI), ported from libtess2 under SGI Free Software License B: it
/// resolves contours that cross themselves and each other into the planar arrangement they describe, keeps the regions
/// a winding rule calls inside, and either triangulates them or returns their boundary as simple contours. Robust to
/// degenerate input: coincident vertices merge, crossings get vertices, and zero-length edges and two-edge faces go.
/// <para>
/// Planar only: the sweep plane is the input's own (s = x, t = y), with no normal to compute. The mesh, the event queue
/// and the edge dictionary are pooled and their storage retained, so tessellating inputs of a steady size allocates
/// nothing once warm.
/// </para>
/// </summary>
internal sealed partial class SweepTessellator
{
	internal enum WindingRule { Odd, Nonzero, Positive }

	private double _minS, _minT, _maxS, _maxT;
	private bool _any;

	/// <summary>The output's vertices: boundary contour points or triangle corners.</summary>
	public RetainedBuffer<Vector2> Positions { get; } = new(64);

	/// <summary>The output's triangles as three indices into <see cref="Positions"/> each.</summary>
	public RetainedBuffer<int> Triangles { get; } = new(192);

	/// <summary>The output's boundary contours as ranges of <see cref="Positions"/>, each with the inside on its left
	/// (a positive signed area round a filled region, negative round a hole).</summary>
	public RetainedBuffer<(int Start, int Count)> Contours { get; } = new(8);

	public SweepTessellator() => NewMesh();

	/// <summary>Start a new input.</summary>
	public void Begin()
	{
		DeleteMesh();
		_any = false;
	}

	/// <summary>Add a closed contour. Each edge adds 1 to the winding number of the region on its left.</summary>
	public void AddContour(ReadOnlySpan<Vector2> points)
	{
		HalfEdge? e = null;
		foreach (var p in points)
		{
			if (e == null)
			{
				// Make a self-loop (one vertex, one edge).
				e = MeshMakeEdge();
				MeshSplice(e, e.Sym);
			}
			else
			{
				// Create a new vertex and edge which immediately follow e in the ordering around the left face.
				MeshSplitEdge(e);
				e = e.Lnext;
			}

			// The new vertex is now e.Org.
			e.Org!.S = p.X;
			e.Org.T = p.Y;

			// The winding of an edge says how the winding number changes as we cross from the edge's right face to its
			// left face. We add the vertices in such an order that a CCW contour will add +1 to the winding number of the
			// region inside the contour.
			e.Winding = 1;
			e.Sym.Winding = -1;

			if (!_any)
			{
				_minS = _maxS = p.X;
				_minT = _maxT = p.Y;
				_any = true;
			}
			else
			{
				_minS = Math.Min(_minS, p.X);
				_maxS = Math.Max(_maxS, p.X);
				_minT = Math.Min(_minT, p.Y);
				_maxT = Math.Max(_maxT, p.Y);
			}
		}
	}

	/// <summary>
	/// Resolve the contours under <paramref name="rule"/>, leaving either the inside's boundary in
	/// <see cref="Contours"/> or its triangles in <see cref="Triangles"/>, with their points in <see cref="Positions"/>.
	/// The input is consumed.
	/// </summary>
	public void Tessellate(WindingRule rule, bool boundaryOnly)
	{
		Positions.Clear();
		Triangles.Clear();
		Contours.Clear();
		if (!_any)
			return;

		_windingRule = rule;
		ComputeInterior(_minS, _minT, _maxS, _maxT);

		if (boundaryOnly)
		{
			// Throw away all edges except those which separate the interior from the exterior.
			MeshKeepOnlyBoundary();
			OutputContours();
		}
		else
		{
			MeshTessellateInterior();
			OutputTriangles();
		}
		DeleteMesh();
		_any = false;
	}

	private void OutputTriangles()
	{
		for (var v = _vHead.Next; v != _vHead; v = v.Next)
			v.N = -1;

		for (var f = _fHead.Next; f != _fHead; f = f.Next)
		{
			if (!f.Inside)
				continue;
			var edge = f.AnEdge!;
			do
			{
				var v = edge.Org!;
				if (v.N < 0)
				{
					v.N = Positions.Count;
					Positions.Add(new Vector2((float)v.S, (float)v.T));
				}
				Triangles.Add(v.N);
				edge = edge.Lnext;
			} while (edge != f.AnEdge);
		}
	}

	private void OutputContours()
	{
		for (var f = _fHead.Next; f != _fHead; f = f.Next)
		{
			if (!f.Inside)
				continue;
			var start = Positions.Count;
			var edge = f.AnEdge!;
			do
			{
				Positions.Add(new Vector2((float)edge.Org!.S, (float)edge.Org.T));
				edge = edge.Lnext;
			} while (edge != f.AnEdge);
			Contours.Add((start, Positions.Count - start));
		}
	}
}
