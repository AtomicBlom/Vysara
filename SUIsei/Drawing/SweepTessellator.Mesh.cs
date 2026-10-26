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

namespace SUIsei.Drawing;

internal sealed partial class SweepTessellator
{
	// The mesh is libtess2's half-edge structure: each edge is two half-edges (Sym), each with its origin vertex, the face
	// on its left, and the next half-edges counter-clockwise round its origin (Onext) and round its left face (Lnext).
	// Vertices, faces and edges are kept in circular lists with dummy heads. Objects are pooled and reused, so
	// tessellating inputs of a steady size allocates nothing once warm.

	internal sealed class Vertex
	{
		public Vertex Next = null!, Prev = null!;
		public HalfEdge? AnEdge;   // a half-edge with this origin
		public double S, T;        // the position on the sweep plane, the input's x and y
		public int PqHandle;       // to allow deletion from the priority queue
		public int N;              // the output index
	}

	internal sealed class Face
	{
		public Face Next = null!, Prev = null!;
		public HalfEdge? AnEdge;   // a half-edge with this left face
		public int N;
		public bool Inside;        // this face is in the polygon interior
	}

	internal sealed class HalfEdge
	{
		public HalfEdge Next = null!;     // the global edge list: prev == Sym.Next
		public HalfEdge Sym = null!;      // the same edge, the other way
		public HalfEdge Onext = null!;    // next edge counter-clockwise round the origin
		public HalfEdge Lnext = null!;    // next edge counter-clockwise round the left face
		public Vertex? Org;
		public Face? Lface;
		public ActiveRegion? ActiveRegion; // the region with this upper edge (the sweep)
		public int Winding;               // change in winding number from the right face to the left
		public bool First;                // the first half-edge of its pair, which the edge list holds

		public Face? Rface { get => Sym.Lface; set => Sym.Lface = value; }
		public Vertex? Dst { get => Sym.Org; set => Sym.Org = value; }
		public HalfEdge Oprev => Sym.Lnext;
		public HalfEdge Lprev => Onext.Sym;
		public HalfEdge Dprev => Lnext.Sym;
		public HalfEdge Rprev => Sym.Onext;
		public HalfEdge Dnext => Rprev.Sym;
		public HalfEdge Rnext => Oprev.Sym;
	}

	// The mesh: dummy heads of the vertex, face and edge lists.
	private readonly Vertex _vHead = new();
	private readonly Face _fHead = new();
	private readonly HalfEdge _eHead = new() { First = true };
	private readonly HalfEdge _eHeadSym = new();

	private readonly Stack<Vertex> _vertexPool = new();
	private readonly Stack<Face> _facePool = new();
	private readonly Stack<HalfEdge> _edgePool = new(); // first half-edges, each with its Sym

	private void NewMesh()
	{
		_vHead.Next = _vHead.Prev = _vHead;
		_vHead.AnEdge = null;
		_fHead.Next = _fHead.Prev = _fHead;
		_fHead.AnEdge = null;
		_fHead.Inside = false;
		_eHead.Next = _eHead;
		_eHead.Sym = _eHeadSym;
		_eHead.Onext = _eHead.Lnext = null!;
		_eHead.Org = null;
		_eHead.Lface = null;
		_eHead.Winding = 0;
		_eHead.ActiveRegion = null;
		_eHeadSym.Next = _eHeadSym;
		_eHeadSym.Sym = _eHead;
		_eHeadSym.Onext = _eHeadSym.Lnext = null!;
		_eHeadSym.Org = null;
		_eHeadSym.Lface = null;
		_eHeadSym.Winding = 0;
		_eHeadSym.ActiveRegion = null;
	}

	// Return every vertex, face and edge of the mesh to the pools.
	private void DeleteMesh()
	{
		for (var v = _vHead.Next; v != _vHead;)
		{
			var next = v.Next;
			FreeVertex(v);
			v = next;
		}
		for (var f = _fHead.Next; f != _fHead;)
		{
			var next = f.Next;
			FreeFace(f);
			f = next;
		}
		for (var e = _eHead.Next; e != _eHead;)
		{
			var next = e.Next;
			FreeEdge(e);
			e = next;
		}
		NewMesh();
	}

	private Vertex AllocVertex() => _vertexPool.Count > 0 ? _vertexPool.Pop() : new Vertex();

	private Face AllocFace() => _facePool.Count > 0 ? _facePool.Pop() : new Face();

	private void FreeVertex(Vertex v)
	{
		v.AnEdge = null;
		_vertexPool.Push(v);
	}

	private void FreeFace(Face f)
	{
		f.AnEdge = null;
		_facePool.Push(f);
	}

	private void FreeEdge(HalfEdge e)
	{
		if (!e.First)
			e = e.Sym;
		e.Org = e.Sym.Org = null;
		e.Lface = e.Sym.Lface = null;
		e.ActiveRegion = e.Sym.ActiveRegion = null;
		_edgePool.Push(e);
	}

	/************************ Utility Routines ************************/

	// MakeEdge creates a new pair of half-edges which form their own loop. No vertex or face structures are allocated,
	// but these must be assigned before the current edge operation is completed.
	private HalfEdge MakeEdge(HalfEdge eNext)
	{
		HalfEdge e, eSym;
		if (_edgePool.Count > 0)
		{
			e = _edgePool.Pop();
			eSym = e.Sym;
		}
		else
		{
			e = new HalfEdge { First = true };
			eSym = new HalfEdge();
		}

		// Make sure eNext points to the first edge of the edge pair
		if (!eNext.First)
			eNext = eNext.Sym;

		// Insert in circular doubly-linked list before eNext. Note that the prev pointer is stored in Sym.Next.
		var ePrev = eNext.Sym.Next;
		eSym.Next = ePrev;
		ePrev.Sym.Next = e;
		e.Next = eNext;
		eNext.Sym.Next = eSym;

		e.Sym = eSym;
		e.Onext = e;
		e.Lnext = eSym;
		e.Org = null;
		e.Lface = null;
		e.Winding = 0;
		e.ActiveRegion = null;

		eSym.Sym = e;
		eSym.Onext = eSym;
		eSym.Lnext = e;
		eSym.Org = null;
		eSym.Lface = null;
		eSym.Winding = 0;
		eSym.ActiveRegion = null;

		return e;
	}

	// Splice(a, b) exchanges a.Onext and b.Onext (see Guibas and Stolfi). This can have various effects depending on
	// whether a and b belong to different face or vertex rings.
	private static void Splice(HalfEdge a, HalfEdge b)
	{
		var aOnext = a.Onext;
		var bOnext = b.Onext;
		aOnext.Sym.Lnext = b;
		bOnext.Sym.Lnext = a;
		a.Onext = bOnext;
		b.Onext = aOnext;
	}

	// MakeVertex attaches a new vertex and makes it the origin of all edges in the vertex loop to which eOrig belongs.
	// It is inserted before vNext, so algorithms which walk the vertex list will not see the new vertices.
	private static void MakeVertex(Vertex vNew, HalfEdge eOrig, Vertex vNext)
	{
		var vPrev = vNext.Prev;
		vNew.Prev = vPrev;
		vPrev.Next = vNew;
		vNew.Next = vNext;
		vNext.Prev = vNew;
		vNew.AnEdge = eOrig;

		var e = eOrig;
		do
		{
			e.Org = vNew;
			e = e.Onext;
		} while (e != eOrig);
	}

	// MakeFace attaches a new face and makes it the left face of all edges in the face loop to which eOrig belongs. It
	// is inserted before fNext, and is marked inside if fNext was (the common case of a face split in two).
	private static void MakeFace(Face fNew, HalfEdge eOrig, Face fNext)
	{
		var fPrev = fNext.Prev;
		fNew.Prev = fPrev;
		fPrev.Next = fNew;
		fNew.Next = fNext;
		fNext.Prev = fNew;
		fNew.AnEdge = eOrig;
		fNew.Inside = fNext.Inside;

		var e = eOrig;
		do
		{
			e.Lface = fNew;
			e = e.Lnext;
		} while (e != eOrig);
	}

	// KillEdge destroys an edge (both half-edges) and removes it from the global edge list.
	private void KillEdge(HalfEdge eDel)
	{
		if (!eDel.First)
			eDel = eDel.Sym;
		var eNext = eDel.Next;
		var ePrev = eDel.Sym.Next;
		eNext.Sym.Next = ePrev;
		ePrev.Sym.Next = eNext;
		FreeEdge(eDel);
	}

	// KillVertex destroys a vertex, removes it from the global vertex list, and makes newOrg the origin of its loop.
	private void KillVertex(Vertex vDel, Vertex? newOrg)
	{
		var eStart = vDel.AnEdge!;
		var e = eStart;
		do
		{
			e.Org = newOrg;
			e = e.Onext;
		} while (e != eStart);

		var vPrev = vDel.Prev;
		var vNext = vDel.Next;
		vNext.Prev = vPrev;
		vPrev.Next = vNext;
		FreeVertex(vDel);
	}

	// KillFace destroys a face, removes it from the global face list, and makes newLface the left face of its loop.
	private void KillFace(Face fDel, Face? newLface)
	{
		var eStart = fDel.AnEdge!;
		var e = eStart;
		do
		{
			e.Lface = newLface;
			e = e.Lnext;
		} while (e != eStart);

		var fPrev = fDel.Prev;
		var fNext = fDel.Next;
		fNext.Prev = fPrev;
		fPrev.Next = fNext;
		FreeFace(fDel);
	}

	/****************** Basic Edge Operations **********************/

	// MeshMakeEdge creates one edge, two vertices, and a loop (face) of the two new half-edges.
	private HalfEdge MeshMakeEdge()
	{
		var e = MakeEdge(_eHead);
		MakeVertex(AllocVertex(), e, _vHead);
		MakeVertex(AllocVertex(), e.Sym, _vHead);
		MakeFace(AllocFace(), e, _fHead);
		return e;
	}

	// MeshSplice(eOrg, eDst) changes the mesh so that eOrg.Onext <- OLD(eDst.Onext) and eDst.Onext <- OLD(eOrg.Onext).
	// Two vertices are merged (eDst.Org destroyed) if they differ, or one is split in two; likewise faces. In both cases
	// eDst.Org and eDst.Lface change and eOrg's are untouched.
	private void MeshSplice(HalfEdge eOrg, HalfEdge eDst)
	{
		var joiningLoops = false;
		var joiningVertices = false;
		if (eOrg == eDst)
			return;

		if (eDst.Org != eOrg.Org)
		{
			// We are merging two disjoint vertices -- destroy eDst.Org
			joiningVertices = true;
			KillVertex(eDst.Org!, eOrg.Org);
		}
		if (eDst.Lface != eOrg.Lface)
		{
			// We are connecting two disjoint loops -- destroy eDst.Lface
			joiningLoops = true;
			KillFace(eDst.Lface!, eOrg.Lface);
		}

		Splice(eDst, eOrg);

		if (!joiningVertices)
		{
			// We split one vertex into two -- the new vertex is eDst.Org. Make sure the old vertex points to a valid edge.
			MakeVertex(AllocVertex(), eDst, eOrg.Org!);
			eOrg.Org!.AnEdge = eOrg;
		}
		if (!joiningLoops)
		{
			// We split one loop into two -- the new loop is eDst.Lface. Make sure the old face points to a valid edge.
			MakeFace(AllocFace(), eDst, eOrg.Lface!);
			eOrg.Lface!.AnEdge = eOrg;
		}
	}

	// MeshDelete(eDel) removes the edge eDel. If its two faces differ they are joined (eDel.Lface is deleted);
	// otherwise the loop is split in two, the new one containing eDel.Dst. Isolated vertices are deleted too.
	private void MeshDelete(HalfEdge eDel)
	{
		var eDelSym = eDel.Sym;
		var joiningLoops = false;

		// First step: disconnect the origin vertex eDel.Org.
		if (eDel.Lface != eDel.Rface)
		{
			joiningLoops = true;
			KillFace(eDel.Lface!, eDel.Rface);
		}

		if (eDel.Onext == eDel)
			KillVertex(eDel.Org!, null);
		else
		{
			// Make sure that eDel.Org and eDel.Rface point to valid half-edges
			eDel.Rface!.AnEdge = eDel.Oprev;
			eDel.Org!.AnEdge = eDel.Onext;
			Splice(eDel, eDel.Oprev);
			if (!joiningLoops)
			{
				// We are splitting one loop into two -- create a new loop for eDel.
				MakeFace(AllocFace(), eDel, eDel.Lface!);
			}
		}

		// The mesh is consistent now, except that eDel.Org may have been deleted. Now disconnect eDel.Dst.
		if (eDelSym.Onext == eDelSym)
		{
			KillVertex(eDelSym.Org!, null);
			KillFace(eDelSym.Lface!, null);
		}
		else
		{
			// Make sure that eDel.Dst and eDel.Lface point to valid half-edges
			eDel.Lface!.AnEdge = eDelSym.Oprev;
			eDelSym.Org!.AnEdge = eDelSym.Onext;
			Splice(eDelSym, eDelSym.Oprev);
		}

		KillEdge(eDel);
	}

	/******************** Other Edge Operations **********************/

	// MeshAddEdgeVertex(eOrg) creates a new edge eNew == eOrg.Lnext whose Dst is a new vertex; both share eOrg's left face.
	private HalfEdge MeshAddEdgeVertex(HalfEdge eOrg)
	{
		var eNew = MakeEdge(eOrg);
		var eNewSym = eNew.Sym;

		Splice(eNew, eOrg.Lnext);

		eNew.Org = eOrg.Dst;
		MakeVertex(AllocVertex(), eNewSym, eNew.Org!);
		eNew.Lface = eNewSym.Lface = eOrg.Lface;
		return eNew;
	}

	// MeshSplitEdge(eOrg) splits eOrg into eOrg and eNew == eOrg.Lnext; the new vertex is eOrg.Dst == eNew.Org.
	private HalfEdge MeshSplitEdge(HalfEdge eOrg)
	{
		var tempHalfEdge = MeshAddEdgeVertex(eOrg);
		var eNew = tempHalfEdge.Sym;

		// Disconnect eOrg from eOrg.Dst and connect it to eNew.Org
		Splice(eOrg.Sym, eOrg.Sym.Oprev);
		Splice(eOrg.Sym, eNew);

		eOrg.Dst = eNew.Org;
		eNew.Dst!.AnEdge = eNew.Sym; // may have pointed to eOrg.Sym
		eNew.Rface = eOrg.Rface;
		eNew.Winding = eOrg.Winding; // copy old winding information
		eNew.Sym.Winding = eOrg.Sym.Winding;
		return eNew;
	}

	// MeshConnect(eOrg, eDst) creates a new edge from eOrg.Dst to eDst.Org and returns it. If the two share a left
	// face, the loop is split and the new loop is eNew.Lface; otherwise two loops join and eDst.Lface is destroyed.
	private HalfEdge MeshConnect(HalfEdge eOrg, HalfEdge eDst)
	{
		var joiningLoops = false;
		var eNew = MakeEdge(eOrg);
		var eNewSym = eNew.Sym;

		if (eDst.Lface != eOrg.Lface)
		{
			joiningLoops = true;
			KillFace(eDst.Lface!, eOrg.Lface);
		}

		Splice(eNew, eOrg.Lnext);
		Splice(eNewSym, eDst);

		eNew.Org = eOrg.Dst;
		eNewSym.Org = eDst.Org;
		eNew.Lface = eNewSym.Lface = eOrg.Lface;

		// Make sure the old face points to a valid half-edge
		eOrg.Lface!.AnEdge = eNewSym;

		if (!joiningLoops)
		{
			// We split one loop into two -- the new loop is eNew.Lface
			MakeFace(AllocFace(), eNew, eOrg.Lface);
		}
		return eNew;
	}

	// MeshZapFace(fZap) destroys a face and removes it from the face list. Its edges get a null left face, and any
	// edge whose right face is null too is deleted, with any vertices this isolates.
	private void MeshZapFace(Face fZap)
	{
		var eStart = fZap.AnEdge!;
		HalfEdge e;
		var eNext = eStart.Lnext;
		do
		{
			e = eNext;
			eNext = e.Lnext;
			e.Lface = null;
			if (e.Rface == null)
			{
				if (e.Onext == e)
					KillVertex(e.Org!, null);
				else
				{
					e.Org!.AnEdge = e.Onext;
					Splice(e, e.Oprev);
				}
				var eSym = e.Sym;
				if (eSym.Onext == eSym)
					KillVertex(eSym.Org!, null);
				else
				{
					eSym.Org!.AnEdge = eSym.Onext;
					Splice(eSym, eSym.Oprev);
				}
				KillEdge(e);
			}
		} while (e != eStart);

		var fPrev = fZap.Prev;
		var fNext = fZap.Next;
		fNext.Prev = fPrev;
		fPrev.Next = fNext;
		FreeFace(fZap);
	}
}
