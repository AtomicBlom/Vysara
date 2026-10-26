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

using System.Diagnostics;

namespace SUIsei.Drawing;

internal sealed partial class SweepTessellator
{
	/*
	* Invariants for the Edge Dictionary.
	* - each pair of adjacent edges e2=Succ(e1) satisfies EdgeLeq(e1,e2) at any valid location of the sweep event
	* - if EdgeLeq(e2,e1) as well (at any valid sweep event), then e1 and e2 share a common endpoint
	* - for each e, e.Dst has been processed, but not e.Org
	* - each edge e satisfies VertLeq(e.Dst,event) && VertLeq(event,e.Org) where "event" is the current sweep event
	* - no edge e has zero length
	*
	* Invariants for the Mesh (the processed portion).
	* - the portion of the mesh left of the sweep line is a planar graph
	* - no processed edge has zero length
	* - no two processed vertices have identical coordinates
	* - each "inside" region is monotone, ie. can be broken into two chains of monotonically increasing vertices
	*   according to VertLeq(v1,v2) (a non-invariant: these chains may intersect very slightly)
	*
	* Invariants for the Sweep.
	* - if none of the edges incident to the event vertex have an activeRegion (ie. none of these edges are in the edge
	*   dictionary), then the vertex has only right-going edges.
	* - if an edge is marked "fixUpperEdge" (it is a temporary edge introduced by ConnectRightVertex), then it is the
	*   only right-going edge from its associated vertex.
	*/

	/// <summary>
	/// The region between a pair of adjacent edges crossing the sweep line. Regions are kept in order in the edge
	/// dictionary; as the sweep line crosses each vertex, the affected regions are updated.
	/// </summary>
	internal sealed class ActiveRegion
	{
		public HalfEdge EUp = null!;    // upper edge, directed right to left
		public DictNode NodeUp = null!; // dictionary node corresponding to EUp
		public int WindingNumber;       // used to determine which regions are inside the polygon
		public bool Inside;             // is this region inside the polygon?
		public bool Sentinel;           // marks fake edges at t = +/-infinity
		public bool Dirty;              // the upper or lower edge has changed, not yet checked for intersection
		public bool FixUpperEdge;       // a temporary edge introduced for a "right vertex" (no edges leaving right)
	}

	/// <summary>A node of the edge dictionary: a sorted, circular, doubly-linked list with a key-less head.</summary>
	internal sealed class DictNode
	{
		public ActiveRegion? Key;
		public DictNode Next = null!, Prev = null!;
	}

	private readonly DictNode _dictHead = new();
	private readonly Stack<DictNode> _dictPool = new();
	private readonly Stack<ActiveRegion> _regionPool = new();
	private readonly PriorityQueue _pq = new();
	private readonly ActiveRegion _searchKey = new();
	private readonly Vertex _isect = new();
	private Vertex _event = null!;
	private WindingRule _windingRule;

	private ActiveRegion RegionBelow(ActiveRegion r) => r.NodeUp.Prev.Key!;

	private static ActiveRegion? RegionAbove(ActiveRegion r) => r.NodeUp.Next.Key;

	// When two edges merge into one, the new edge's winding is the sum.
	private static void AddWinding(HalfEdge eDst, HalfEdge eSrc)
	{
		eDst.Winding += eSrc.Winding;
		eDst.Sym.Winding += eSrc.Sym.Winding;
	}

	/************************ The edge dictionary ************************/

	private void DictInit()
	{
		_dictHead.Key = null;
		_dictHead.Next = _dictHead.Prev = _dictHead;
	}

	private DictNode DictInsertBefore(DictNode node, ActiveRegion key)
	{
		do
		{
			node = node.Prev;
		} while (node.Key != null && !EdgeLeq(node.Key, key));

		var newNode = _dictPool.Count > 0 ? _dictPool.Pop() : new DictNode();
		newNode.Key = key;
		newNode.Next = node.Next;
		node.Next.Prev = newNode;
		newNode.Prev = node;
		node.Next = newNode;
		return newNode;
	}

	private void DictDelete(DictNode node)
	{
		node.Next.Prev = node.Prev;
		node.Prev.Next = node.Next;
		node.Key = null;
		_dictPool.Push(node);
	}

	// The node with the smallest key >= the given key, or the head (a null key) if there is none.
	private DictNode DictSearch(ActiveRegion key)
	{
		var node = _dictHead;
		do
		{
			node = node.Next;
		} while (node.Key != null && !EdgeLeq(key, node.Key));
		return node;
	}

	/*
	* Both edges must be directed from right to left (the canonical direction for the upper edge of each region). The
	* strategy is to evaluate a "t" value for each edge at the current sweep line position, given by _event. The
	* calculations are designed to be very stable, but of course they are not perfect.
	*
	* Special case: if both edge destinations are at the sweep event, we sort the edges by slope (they would otherwise
	* compare equally).
	*/
	private bool EdgeLeq(ActiveRegion reg1, ActiveRegion reg2)
	{
		var ev = _event;
		var e1 = reg1.EUp;
		var e2 = reg2.EUp;

		if (e1.Dst == ev)
		{
			if (e2.Dst == ev)
			{
				// Two edges right of the sweep line which meet at the sweep event. Sort them by slope.
				if (VertLeq(e1.Org!, e2.Org!))
					return EdgeSign(e2.Dst!, e1.Org!, e2.Org!) <= 0;
				return EdgeSign(e1.Dst!, e2.Org!, e1.Org!) >= 0;
			}
			return EdgeSign(e2.Dst!, ev, e2.Org!) <= 0;
		}
		if (e2.Dst == ev)
			return EdgeSign(e1.Dst!, ev, e1.Org!) >= 0;

		// General case - compute signed distance *from* e1, e2 to event
		var t1 = EdgeEval(e1.Dst!, ev, e1.Org!);
		var t2 = EdgeEval(e2.Dst!, ev, e2.Org!);
		return t1 >= t2;
	}

	/************************ The sweep ************************/

	private void DeleteRegion(ActiveRegion reg)
	{
		// A fixable edge was created with zero winding number, so it must be deleted with zero winding number.
		Debug.Assert(!reg.FixUpperEdge || reg.EUp.Winding == 0);
		reg.EUp.ActiveRegion = null;
		DictDelete(reg.NodeUp);
		reg.EUp = null!;
		reg.NodeUp = null!;
		_regionPool.Push(reg);
	}

	// Replace an upper edge which needs fixing (see ConnectRightVertex).
	private void FixUpperEdge(ActiveRegion reg, HalfEdge newEdge)
	{
		Debug.Assert(reg.FixUpperEdge);
		MeshDelete(reg.EUp);
		reg.FixUpperEdge = false;
		reg.EUp = newEdge;
		newEdge.ActiveRegion = reg;
	}

	private ActiveRegion TopLeftRegion(ActiveRegion reg)
	{
		var org = reg.EUp.Org;

		// Find the region above the uppermost edge with the same origin
		do
		{
			reg = RegionAbove(reg)!;
		} while (reg.EUp.Org == org);

		// If the edge above was a temporary edge introduced by ConnectRightVertex, now is the time to fix it.
		if (reg.FixUpperEdge)
		{
			var e = MeshConnect(RegionBelow(reg).EUp.Sym, reg.EUp.Lnext);
			FixUpperEdge(reg, e);
			reg = RegionAbove(reg)!;
		}
		return reg;
	}

	private static ActiveRegion TopRightRegion(ActiveRegion reg)
	{
		var dst = reg.EUp.Dst;

		// Find the region above the uppermost edge with the same destination
		do
		{
			reg = RegionAbove(reg)!;
		} while (reg.EUp.Dst == dst);
		return reg;
	}

	// Add a new active region to the sweep line, somewhere below regAbove (where the new edge belongs in the
	// dictionary). The upper edge of the new region will be eNewUp. Winding number and "inside" are not updated.
	private ActiveRegion AddRegionBelow(ActiveRegion regAbove, HalfEdge eNewUp)
	{
		var regNew = _regionPool.Count > 0 ? _regionPool.Pop() : new ActiveRegion();
		regNew.EUp = eNewUp;
		regNew.NodeUp = DictInsertBefore(regAbove.NodeUp, regNew);
		regNew.FixUpperEdge = false;
		regNew.Sentinel = false;
		regNew.Dirty = false;
		regNew.WindingNumber = 0;
		regNew.Inside = false;
		eNewUp.ActiveRegion = regNew;
		return regNew;
	}

	private bool IsWindingInside(int n) => _windingRule switch
	{
		WindingRule.Odd => (n & 1) != 0,
		WindingRule.Nonzero => n != 0,
		_ => n > 0,
	};

	private void ComputeWinding(ActiveRegion reg)
	{
		reg.WindingNumber = RegionAbove(reg)!.WindingNumber + reg.EUp.Winding;
		reg.Inside = IsWindingInside(reg.WindingNumber);
	}

	// Delete a region from the sweep line, where its upper and lower chains meet (at a vertex on the sweep line). The
	// "inside" flag is copied to the mesh face, which may not have existed until now.
	private void FinishRegion(ActiveRegion reg)
	{
		var e = reg.EUp;
		var f = e.Lface!;
		f.Inside = reg.Inside;
		f.AnEdge = e; // an optimization for MeshTessellateMonoRegion
		DeleteRegion(reg);
	}

	/*
	* We are given a vertex with one or more left-going edges. All affected edges should be in the edge dictionary.
	* Starting at regFirst.EUp, we walk down deleting all regions where both edges have the same origin vOrg. At the
	* same time we copy the "inside" flag from the active region to the face, since at this point each face will belong
	* to at most one region. The walk stops at the region above regLast; if regLast is null we walk as far as possible.
	* At the same time we relink the mesh if necessary, so that the ordering of edges around vOrg is the same as in the
	* dictionary.
	*/
	private HalfEdge FinishLeftRegions(ActiveRegion regFirst, ActiveRegion? regLast)
	{
		var regPrev = regFirst;
		var ePrev = regFirst.EUp;
		while (regPrev != regLast)
		{
			regPrev.FixUpperEdge = false; // placement was OK
			var reg = RegionBelow(regPrev);
			var e = reg.EUp;
			if (e.Org != ePrev.Org)
			{
				if (!reg.FixUpperEdge)
				{
					// Remove the last left-going edge. There may be further such edges in the mesh (if we are adding left
					// edges to a vertex that has already been processed), so FinishRegion rather than DeleteRegion.
					FinishRegion(regPrev);
					break;
				}
				// If the edge below was a temporary edge introduced by ConnectRightVertex, now is the time to fix it.
				e = MeshConnect(ePrev.Lprev, e.Sym);
				FixUpperEdge(reg, e);
			}

			// Relink edges so that ePrev.Onext == e
			if (ePrev.Onext != e)
			{
				MeshSplice(e.Oprev, e);
				MeshSplice(ePrev, e);
			}
			FinishRegion(regPrev); // may change reg.EUp
			ePrev = reg.EUp;
			regPrev = reg;
		}
		return ePrev;
	}

	/*
	* Insert right-going edges into the edge dictionary, and update winding numbers and mesh connectivity
	* appropriately. All right-going edges share a common origin vOrg. Edges are inserted CCW starting at eFirst; the
	* last edge inserted is eLast.Oprev. If vOrg has any left-going edges already processed, then eTopLeft must be the
	* edge such that an imaginary upward vertical segment from vOrg would be contained between eTopLeft.Oprev and
	* eTopLeft; otherwise eTopLeft should be null.
	*/
	private void AddRightEdges(ActiveRegion regUp, HalfEdge eFirst, HalfEdge eLast, HalfEdge? eTopLeft, bool cleanUp)
	{
		var firstTime = true;

		// Insert the new right-going edges in the dictionary
		var e = eFirst;
		do
		{
			Debug.Assert(VertLeq(e.Org!, e.Dst!));
			AddRegionBelow(regUp, e.Sym);
			e = e.Onext;
		} while (e != eLast);

		// Walk *all* right-going edges from e.Org, in the dictionary order, updating the winding numbers of each region,
		// and re-linking the mesh edges to match the dictionary ordering (if necessary).
		eTopLeft ??= RegionBelow(regUp).EUp.Rprev;
		var regPrev = regUp;
		var ePrev = eTopLeft;
		ActiveRegion reg;
		for (; ; )
		{
			reg = RegionBelow(regPrev);
			e = reg.EUp.Sym;
			if (e.Org != ePrev.Org)
				break;

			if (e.Onext != ePrev)
			{
				// Unlink e from its current position, and relink below ePrev
				MeshSplice(e.Oprev, e);
				MeshSplice(ePrev.Oprev, e);
			}
			// Compute the winding number and "inside" flag for the new regions
			reg.WindingNumber = regPrev.WindingNumber - e.Winding;
			reg.Inside = IsWindingInside(reg.WindingNumber);

			// Check for two outgoing edges with same slope -- process these before any intersection tests.
			regPrev.Dirty = true;
			if (!firstTime && CheckForRightSplice(regPrev))
			{
				AddWinding(e, ePrev);
				DeleteRegion(regPrev);
				MeshDelete(ePrev);
			}
			firstTime = false;
			regPrev = reg;
			ePrev = e;
		}
		regPrev.Dirty = true;
		Debug.Assert(regPrev.WindingNumber - e.Winding == reg.WindingNumber);

		if (cleanUp)
		{
			// Check for intersections between newly adjacent edges.
			WalkDirtyRegions(regPrev);
		}
	}

	// Two vertices with identical coordinates are combined into one: e1.Org is kept, e2.Org is discarded.
	private void SpliceMergeVertices(HalfEdge e1, HalfEdge e2) => MeshSplice(e1, e2);

	/*
	* Check the upper and lower edge of regUp, to make sure that the eUp.Org is above eLo, or eLo.Org is below eUp
	* (depending on which origin is leftmost).
	*
	* The main purpose is to splice right-going edges with the same dest vertex and nearly identical slopes (ie. we
	* can't distinguish the slopes numerically). However the splicing can also help us to recover from numerical
	* errors. Where CheckForIntersect cannot be used (it needs the event between eUp and eLo), this fixes the problem by
	* splicing the offending vertex into the other edge: a combinatorial solution to a numerical problem.
	*/
	private bool CheckForRightSplice(ActiveRegion regUp)
	{
		var regLo = RegionBelow(regUp);
		var eUp = regUp.EUp;
		var eLo = regLo.EUp;

		if (VertLeq(eUp.Org!, eLo.Org!))
		{
			if (EdgeSign(eLo.Dst!, eUp.Org!, eLo.Org!) > 0)
				return false;

			// eUp.Org appears to be below eLo
			if (!VertEq(eUp.Org!, eLo.Org!))
			{
				// Splice eUp.Org into eLo
				MeshSplitEdge(eLo.Sym);
				MeshSplice(eUp, eLo.Oprev);
				regUp.Dirty = regLo.Dirty = true;
			}
			else if (eUp.Org != eLo.Org)
			{
				// merge the two vertices, discarding eUp.Org
				_pq.Delete(eUp.Org!.PqHandle);
				SpliceMergeVertices(eLo.Oprev, eUp);
			}
		}
		else
		{
			if (EdgeSign(eUp.Dst!, eLo.Org!, eUp.Org!) < 0)
				return false;

			// eLo.Org appears to be above eUp, so splice eLo.Org into eUp
			regUp.Dirty = true;
			if (RegionAbove(regUp) is { } regionAbove)
				regionAbove.Dirty = true;
			MeshSplitEdge(eUp.Sym);
			MeshSplice(eLo.Oprev, eUp);
		}
		return true;
	}

	/*
	* Check the upper and lower edge of regUp, to make sure that the eUp.Dst is above eLo, or eLo.Dst is below eUp
	* (depending on which destination is rightmost). Splitting an edge can change the results of earlier tests; we fix
	* any violation by splicing the offending vertex into the other edge, to keep the dictionary invariants.
	*/
	private bool CheckForLeftSplice(ActiveRegion regUp)
	{
		var regLo = RegionBelow(regUp);
		var eUp = regUp.EUp;
		var eLo = regLo.EUp;
		HalfEdge e;

		Debug.Assert(!VertEq(eUp.Dst!, eLo.Dst!));

		if (VertLeq(eUp.Dst!, eLo.Dst!))
		{
			if (EdgeSign(eUp.Dst!, eLo.Dst!, eUp.Org!) < 0)
				return false;

			// eLo.Dst is above eUp, so splice eLo.Dst into eUp
			regUp.Dirty = true;
			if (RegionAbove(regUp) is { } regionAbove)
				regionAbove.Dirty = true;
			e = MeshSplitEdge(eUp);
			MeshSplice(eLo.Sym, e);
			e.Lface!.Inside = regUp.Inside;
		}
		else
		{
			if (EdgeSign(eLo.Dst!, eUp.Dst!, eLo.Org!) > 0)
				return false;

			// eUp.Dst is below eLo, so splice eUp.Dst into eLo
			regUp.Dirty = regLo.Dirty = true;
			e = MeshSplitEdge(eLo);
			MeshSplice(eUp.Lnext, eLo.Sym);
			e.Rface!.Inside = regUp.Inside;
		}
		return true;
	}

	/*
	* Check the upper and lower edges of the given region to see if they intersect. If so, create the intersection and
	* add it to the data structures. Returns true if adding the new intersection resulted in a recursive call to
	* AddRightEdges(); in this case all "dirty" regions have been checked for intersections, and possibly regUp has been
	* deleted.
	*/
	private bool CheckForIntersect(ActiveRegion regUp)
	{
		var regLo = RegionBelow(regUp);
		var eUp = regUp.EUp;
		var eLo = regLo.EUp;
		var orgUp = eUp.Org!;
		var orgLo = eLo.Org!;
		var dstUp = eUp.Dst!;
		var dstLo = eLo.Dst!;
		var isect = _isect;

		Debug.Assert(!VertEq(dstLo, dstUp));
		Debug.Assert(EdgeSign(dstUp, _event, orgUp) <= 0);
		Debug.Assert(EdgeSign(dstLo, _event, orgLo) >= 0);
		Debug.Assert(orgUp != _event && orgLo != _event);
		Debug.Assert(!regUp.FixUpperEdge && !regLo.FixUpperEdge);

		if (orgUp == orgLo)
			return false; // right endpoints are the same

		var tMinUp = Math.Min(orgUp.T, dstUp.T);
		var tMaxLo = Math.Max(orgLo.T, dstLo.T);
		if (tMinUp > tMaxLo)
			return false; // t ranges do not overlap

		if (VertLeq(orgUp, orgLo))
		{
			if (EdgeSign(dstLo, orgUp, orgLo) > 0)
				return false;
		}
		else
		{
			if (EdgeSign(dstUp, orgLo, orgUp) < 0)
				return false;
		}

		// At this point the edges intersect, at least marginally
		EdgeIntersect(dstUp, orgUp, dstLo, orgLo, out isect.S, out isect.T);

		if (VertLeq(isect, _event))
		{
			// The intersection point lies slightly to the left of the sweep line, so move it until it's slightly to the
			// right of the sweep line. The easiest and safest thing to do is replace the intersection by the event.
			isect.S = _event.S;
			isect.T = _event.T;
		}
		// Similarly, if the computed intersection lies to the right of the rightmost origin (which should rarely
		// happen), it can cause unbelievable inefficiency on sufficiently degenerate inputs.
		var orgMin = VertLeq(orgUp, orgLo) ? orgUp : orgLo;
		if (VertLeq(orgMin, isect))
		{
			isect.S = orgMin.S;
			isect.T = orgMin.T;
		}

		if (VertEq(isect, orgUp) || VertEq(isect, orgLo))
		{
			// Easy case -- intersection at one of the right endpoints
			CheckForRightSplice(regUp);
			return false;
		}

		if ((!VertEq(dstUp, _event) && EdgeSign(dstUp, _event, isect) >= 0)
			|| (!VertEq(dstLo, _event) && EdgeSign(dstLo, _event, isect) <= 0))
		{
			// Very unusual -- the new upper or lower edge would pass on the wrong side of the sweep event, or through it.
			// This can happen due to very small numerical errors in the intersection calculation.
			if (dstLo == _event)
			{
				// Splice dstLo into eUp, and process the new region(s)
				MeshSplitEdge(eUp.Sym);
				MeshSplice(eLo.Sym, eUp);
				regUp = TopLeftRegion(regUp);
				eUp = RegionBelow(regUp).EUp;
				FinishLeftRegions(RegionBelow(regUp), regLo);
				AddRightEdges(regUp, eUp.Oprev, eUp, eUp, true);
				return true;
			}
			if (dstUp == _event)
			{
				// Splice dstUp into eLo, and process the new region(s)
				MeshSplitEdge(eLo.Sym);
				MeshSplice(eUp.Lnext, eLo.Oprev);
				regLo = regUp;
				regUp = TopRightRegion(regUp);
				var e = RegionBelow(regUp).EUp.Rprev;
				regLo.EUp = eLo.Oprev;
				eLo = FinishLeftRegions(regLo, null);
				AddRightEdges(regUp, eLo.Onext, eUp.Rprev, e, true);
				return true;
			}
			// Special case: called from ConnectRightVertex. If either edge passes on the wrong side of the event, split
			// it (and wait for ConnectRightVertex to splice it appropriately).
			if (EdgeSign(dstUp, _event, isect) >= 0)
			{
				RegionAbove(regUp)!.Dirty = regUp.Dirty = true;
				MeshSplitEdge(eUp.Sym);
				eUp.Org!.S = _event.S;
				eUp.Org.T = _event.T;
			}
			if (EdgeSign(dstLo, _event, isect) <= 0)
			{
				regUp.Dirty = regLo.Dirty = true;
				MeshSplitEdge(eLo.Sym);
				eLo.Org!.S = _event.S;
				eLo.Org.T = _event.T;
			}
			// leave the rest for ConnectRightVertex
			return false;
		}

		// General case -- split both edges, splice into new vertex. When the operation creates a new face, the work done
		// is proportional to the size of the new face; we expect the faces in the processed part of the mesh (eUp.Lface)
		// to be smaller than the faces in the unprocessed original contours (eLo.Oprev.Lface).
		MeshSplitEdge(eUp.Sym);
		MeshSplitEdge(eLo.Sym);
		MeshSplice(eLo.Oprev, eUp);
		eUp.Org!.S = isect.S;
		eUp.Org.T = isect.T;
		eUp.Org.PqHandle = _pq.Insert(eUp.Org);
		RegionAbove(regUp)!.Dirty = regUp.Dirty = regLo.Dirty = true;
		return false;
	}

	/*
	* When the upper or lower edge of any region changes, the region is marked "dirty". This routine walks through all
	* the dirty regions and makes sure that the dictionary invariants are satisfied. Of course new dirty regions can be
	* created as we make changes to restore the invariants.
	*/
	private void WalkDirtyRegions(ActiveRegion regUp)
	{
		var regLo = RegionBelow(regUp);

		for (; ; )
		{
			// Find the lowest dirty region (we walk from the bottom up).
			while (regLo.Dirty)
			{
				regUp = regLo;
				regLo = RegionBelow(regLo);
			}
			if (!regUp.Dirty)
			{
				regLo = regUp;
				var above = RegionAbove(regUp);
				if (above == null || !above.Dirty)
					return; // We've walked all the dirty regions
				regUp = above;
			}
			regUp.Dirty = false;
			var eUp = regUp.EUp;
			var eLo = regLo.EUp;

			if (eUp.Dst != eLo.Dst)
			{
				// Check that the edge ordering is obeyed at the Dst vertices.
				if (CheckForLeftSplice(regUp))
				{
					// If the upper or lower edge was marked fixUpperEdge, then we no longer need it (since these edges are
					// needed only for vertices which otherwise have no right-going edges).
					if (regLo.FixUpperEdge)
					{
						DeleteRegion(regLo);
						MeshDelete(eLo);
						regLo = RegionBelow(regUp);
						eLo = regLo.EUp;
					}
					else if (regUp.FixUpperEdge)
					{
						DeleteRegion(regUp);
						MeshDelete(eUp);
						regUp = RegionAbove(regLo)!;
						eUp = regUp.EUp;
					}
				}
			}
			if (eUp.Org != eLo.Org)
			{
				if (eUp.Dst != eLo.Dst && !regUp.FixUpperEdge && !regLo.FixUpperEdge && (eUp.Dst == _event || eLo.Dst == _event))
				{
					// When all else fails in CheckForIntersect(), it uses the event as the intersection location. To make
					// this possible, it requires that the event lie between the upper and lower edges, and also that
					// neither of these is marked fixUpperEdge.
					if (CheckForIntersect(regUp))
						return; // WalkDirtyRegions() was called recursively; we're done
				}
				else
				{
					// Even though we can't use CheckForIntersect(), the Org vertices may violate the dictionary edge
					// ordering. Check and correct this.
					CheckForRightSplice(regUp);
				}
			}
			if (eUp.Org == eLo.Org && eUp.Dst == eLo.Dst)
			{
				// A degenerate loop consisting of only two edges -- delete it.
				AddWinding(eLo, eUp);
				DeleteRegion(regUp);
				MeshDelete(eUp);
				regUp = RegionAbove(regLo)!;
			}
		}
	}

	/*
	* Connect a "right" vertex vEvent (one where all edges go left) to the unprocessed portion of the mesh. Since there
	* are no right-going edges, two regions (one above vEvent and one below) are being merged into one; regUp is the
	* upper of these two regions. If they are inside, an edge keeps them separated (the combined region would not be
	* monotone); in any case some record of vEvent must stay in the dictionary, to merge it with features not yet seen.
	* We connect vEvent to the closest vertex of either chain, and mark the region "fixUpperEdge", to be reconnected to
	* the next processed vertex on the boundary of the combined region.
	*/
	private void ConnectRightVertex(ActiveRegion regUp, HalfEdge eBottomLeft)
	{
		var eTopLeft = eBottomLeft.Onext;
		var regLo = RegionBelow(regUp);
		var eUp = regUp.EUp;
		var eLo = regLo.EUp;
		var degenerate = false;

		if (eUp.Dst != eLo.Dst)
			CheckForIntersect(regUp);

		// Possible new degeneracies: upper or lower edge of regUp may pass through vEvent, or may coincide with new
		// intersection vertex
		if (VertEq(eUp.Org!, _event))
		{
			MeshSplice(eTopLeft.Oprev, eUp);
			regUp = TopLeftRegion(regUp);
			eTopLeft = RegionBelow(regUp).EUp;
			FinishLeftRegions(RegionBelow(regUp), regLo);
			degenerate = true;
		}
		if (VertEq(eLo.Org!, _event))
		{
			MeshSplice(eBottomLeft, eLo.Oprev);
			eBottomLeft = FinishLeftRegions(regLo, null);
			degenerate = true;
		}
		if (degenerate)
		{
			AddRightEdges(regUp, eBottomLeft.Onext, eTopLeft, eTopLeft, true);
			return;
		}

		// Non-degenerate situation -- need to add a temporary, fixable edge. Connect to the closer of eLo.Org, eUp.Org.
		var eNew = VertLeq(eLo.Org!, eUp.Org!) ? eLo.Oprev : eUp;
		eNew = MeshConnect(eBottomLeft.Lprev, eNew);

		// Prevent cleanup, otherwise eNew might disappear before we've even had a chance to mark it as a temporary edge.
		AddRightEdges(regUp, eNew, eNew.Onext, eNew.Onext, false);
		eNew.Sym.ActiveRegion!.FixUpperEdge = true;
		WalkDirtyRegions(regUp);
	}

	/*
	* The event vertex lies exacty on an already-processed edge or vertex. Adding the new vertex involves splicing it
	* into the already-processed part of the mesh. (Vertices at identical positions are merged before each event, so the
	* cases of an equal origin or destination cannot occur.)
	*/
	private void ConnectLeftDegenerate(ActiveRegion regUp, Vertex vEvent)
	{
		var e = regUp.EUp;
		if (VertEq(e.Org!, vEvent))
		{
			// e.Org is an unprocessed vertex - just combine them, and wait for e.Org to be pulled from the queue
			SpliceMergeVertices(e, vEvent.AnEdge!);
			return;
		}

		if (!VertEq(e.Dst!, vEvent))
		{
			// General case -- splice vEvent into edge e which passes through it
			MeshSplitEdge(e.Sym);
			if (regUp.FixUpperEdge)
			{
				// This edge was fixable -- delete unused portion of original edge
				MeshDelete(e.Onext);
				regUp.FixUpperEdge = false;
			}
			MeshSplice(vEvent.AnEdge!, e);
			SweepEvent(vEvent); // recurse
			return;
		}

		// vEvent coincides with e.Dst, which has already been processed. Splice in the additional right-going edges.
		regUp = TopRightRegion(regUp);
		var reg = RegionBelow(regUp);
		var eTopRight = reg.EUp.Sym;
		HalfEdge? eTopLeft = eTopRight.Onext;
		var eLast = eTopLeft;
		if (reg.FixUpperEdge)
		{
			// Here e.Dst has only a single fixable edge going right. We can delete it since now we have some real
			// right-going edges.
			Debug.Assert(eTopLeft != eTopRight); // there are some left edges too
			DeleteRegion(reg);
			MeshDelete(eTopRight);
			eTopRight = eTopLeft.Oprev;
		}
		MeshSplice(vEvent.AnEdge!, eTopRight);
		if (!EdgeGoesLeft(eTopLeft))
		{
			// e.Dst had no left-going edges -- indicate this to AddRightEdges()
			eTopLeft = null;
		}
		AddRightEdges(regUp, eTopRight.Onext, eLast, eTopLeft, true);
	}

	/*
	* Connect a "left" vertex (one where both edges go right) to the processed portion of the mesh. Let R be the active
	* region containing vEvent, and let U and L be the upper and lower edge chains of R. Normally R is split in two by
	* connecting vEvent to the rightmost vertex of U or L lying to the left of the sweep line; if vEvent is close enough
	* to U or L, it is merged into that edge chain instead.
	*/
	private void ConnectLeftVertex(Vertex vEvent)
	{
		// Get the active region containing vEvent
		_searchKey.EUp = vEvent.AnEdge!.Sym;
		var regUp = DictSearch(_searchKey).Key!;
		_searchKey.EUp = null!;
		var regLo = regUp.NodeUp.Prev.Key;
		if (regLo == null)
		{
			// This may happen if the input polygon is coplanar.
			return;
		}
		var eUp = regUp.EUp;
		var eLo = regLo.EUp;

		// Try merging with U or L first
		if (EdgeSign(eUp.Dst!, vEvent, eUp.Org!) == 0)
		{
			ConnectLeftDegenerate(regUp, vEvent);
			return;
		}

		// Connect vEvent to rightmost processed vertex of either chain. e.Dst is the vertex that we will connect to vEvent.
		var reg = VertLeq(eLo.Dst!, eUp.Dst!) ? regUp : regLo;

		if (regUp.Inside || reg.FixUpperEdge)
		{
			HalfEdge eNew;
			if (reg == regUp)
				eNew = MeshConnect(vEvent.AnEdge.Sym, eUp.Lnext);
			else
				eNew = MeshConnect(eLo.Dnext, vEvent.AnEdge).Sym;
			if (reg.FixUpperEdge)
				FixUpperEdge(reg, eNew);
			else
				ComputeWinding(AddRegionBelow(regUp, eNew));
			SweepEvent(vEvent);
		}
		else
		{
			// The new vertex is in a region which does not belong to the polygon. We don't need to connect this vertex
			// to the rest of the mesh.
			AddRightEdges(regUp, vEvent.AnEdge, vEvent.AnEdge, null, true);
		}
	}

	// Everything necessary when the sweep line crosses a vertex: update the mesh and the edge dictionary.
	private void SweepEvent(Vertex vEvent)
	{
		_event = vEvent; // for access in EdgeLeq()

		// Check if this vertex is the right endpoint of an edge that is already in the dictionary. In this case we don't
		// need to waste time searching for the location to insert new edges.
		var e = vEvent.AnEdge!;
		while (e.ActiveRegion == null)
		{
			e = e.Onext;
			if (e == vEvent.AnEdge)
			{
				// All edges go right -- not incident to any processed edges
				ConnectLeftVertex(vEvent);
				return;
			}
		}

		// First "finish" all the active regions where both the upper and lower edges terminate at vEvent, marking their
		// faces inside or outside by winding number and deleting the edges from the dictionary: the left-going edges.
		var regUp = TopLeftRegion(e.ActiveRegion);
		var reg = RegionBelow(regUp);
		var eTopLeft = reg.EUp;
		var eBottomLeft = FinishLeftRegions(reg, null);

		// Next process all the right-going edges from vEvent: add them to the dictionary, with their active regions.
		if (eBottomLeft.Onext == eTopLeft)
		{
			// No right-going edges -- add a temporary "fixable" edge
			ConnectRightVertex(regUp, eBottomLeft);
		}
		else
			AddRightEdges(regUp, eBottomLeft.Onext, eTopLeft, eTopLeft, true);
	}

	// Two sentinel edges above and below all other edges avoid special cases at the top and bottom.
	private void AddSentinel(double smin, double smax, double t)
	{
		var reg = _regionPool.Count > 0 ? _regionPool.Pop() : new ActiveRegion();
		var e = MeshMakeEdge();
		e.Org!.S = smax;
		e.Org.T = t;
		e.Dst!.S = smin;
		e.Dst.T = t;
		_event = e.Dst; // initialize it

		reg.EUp = e;
		reg.WindingNumber = 0;
		reg.Inside = false;
		reg.FixUpperEdge = false;
		reg.Sentinel = true;
		reg.Dirty = false;
		reg.NodeUp = DictInsertBefore(_dictHead, reg);
	}

	// The ordering of edge intersections with the sweep line, in a dynamic dictionary, between two sentinels.
	private void InitEdgeDict(double minS, double minT, double maxS, double maxT)
	{
		DictInit();

		// If the bounds are empty, keep the sentinels apart by slightly enlarging them.
		var w = maxS - minS + 0.01;
		var h = maxT - minT + 0.01;
		AddSentinel(minS - w, maxS + w, minT - h);
		AddSentinel(minS - w, maxS + w, maxT + h);
	}

	// At the end of all processing, the dictionary holds only the two sentinel edges, plus at most one "fixable" edge.
	private void DoneEdgeDict()
	{
		while (_dictHead.Next.Key is { } reg)
		{
			Debug.Assert(reg.Sentinel || reg.FixUpperEdge);
			Debug.Assert(reg.WindingNumber == 0);
			DeleteRegion(reg);
		}
	}

	// Remove zero-length edges, and contours with fewer than 3 vertices.
	private void RemoveDegenerateEdges()
	{
		HalfEdge eNext;
		for (var e = _eHead.Next; e != _eHead; e = eNext)
		{
			eNext = e.Next;
			var eLnext = e.Lnext;

			if (VertEq(e.Org!, e.Dst!) && e.Lnext.Lnext != e)
			{
				// Zero-length edge, contour has at least 3 edges
				SpliceMergeVertices(eLnext, e); // deletes e.Org
				MeshDelete(e); // e is a self-loop
				e = eLnext;
				eLnext = e.Lnext;
			}
			if (eLnext.Lnext == e)
			{
				// Degenerate contour (one or two edges)
				if (eLnext != e)
				{
					if (eLnext == eNext || eLnext == eNext.Sym)
						eNext = eNext.Next;
					MeshDelete(eLnext);
				}
				if (e == eNext || e == eNext.Sym)
					eNext = eNext.Next;
				MeshDelete(e);
			}
		}
	}

	// Insert all vertices into the priority queue which determines the order in which vertices cross the sweep line.
	private void InitPriorityQ()
	{
		var vertexCount = 0;
		for (var v = _vHead.Next; v != _vHead; v = v.Next)
			vertexCount++;
		// Make sure there is enough space for sentinels.
		vertexCount += 8;

		_pq.Reset(vertexCount);
		for (var v = _vHead.Next; v != _vHead; v = v.Next)
			v.PqHandle = _pq.Insert(v);
		_pq.Init();
	}

	/*
	* Delete any degenerate faces with only two edges. WalkDirtyRegions() will catch almost all of these, but it won't
	* catch degenerate faces produced by splice operations on already-processed edges (in FinishLeftRegions and
	* CheckForLeftSplice), where it is dangerous to delete the offending edge at the time.
	*/
	private void RemoveDegenerateFaces()
	{
		Face fNext;
		for (var f = _fHead.Next; f != _fHead; f = fNext)
		{
			fNext = f.Next;
			var e = f.AnEdge!;
			Debug.Assert(e.Lnext != e);

			if (e.Lnext.Lnext == e)
			{
				// A face with only two edges
				AddWinding(e.Onext, e);
				MeshDelete(e);
			}
		}
	}

	/*
	* Compute the planar arrangement specified by the contours, and subdivide it into regions. Each region is marked
	* "inside" if it belongs to the polygon, according to the winding rule. Each interior region is monotone.
	*/
	private void ComputeInterior(double minS, double minT, double maxS, double maxT)
	{
		// Each vertex defines an event for our sweep line, processed in lexicographic order:
		// e1 < e2 iff e1.x < e2.x || (e1.x == e2.x && e1.y < e2.y)
		RemoveDegenerateEdges();
		InitPriorityQ();
		InitEdgeDict(minS, minT, maxS, maxT);

		while (_pq.ExtractMin() is { } v)
		{
			for (; ; )
			{
				var vNext = _pq.Minimum();
				if (vNext == null || !VertEq(vNext, v))
					break;

				// Merge together all vertices at exactly the same location. This is more efficient than processing them one
				// at a time, simplifies the code (see ConnectLeftDegenerate), and is also important for correct handling of
				// certain degenerate cases, such as two identical edges of different contours crossed by a third.
				vNext = _pq.ExtractMin()!;
				SpliceMergeVertices(v.AnEdge!, vNext.AnEdge!);
			}
			SweepEvent(v);
		}

		DoneEdgeDict();
		_pq.Clear();
		RemoveDegenerateFaces();
	}

	/*
	* Tessellate a monotone region: a single loop of half-edges oriented counter-clockwise, which any vertical line
	* crosses in a single interval. Interior edges split it into non-overlapping triangles: both chains are processed
	* from right to left, forming the largest fan possible from the rightmost of the two chain ends at each step.
	*/
	private void MeshTessellateMonoRegion(Face face)
	{
		// All edges are oriented CCW around the boundary of the region. First, find the half-edge whose origin vertex is
		// rightmost. Since the sweep goes from left to right, face.AnEdge should be close to the edge we want.
		var up = face.AnEdge!;
		Debug.Assert(up.Lnext != up && up.Lnext.Lnext != up);

		for (; VertLeq(up.Dst!, up.Org!); up = up.Lprev) { }
		for (; VertLeq(up.Org!, up.Dst!); up = up.Lnext) { }
		var lo = up.Lprev;

		while (up.Lnext != lo)
		{
			if (VertLeq(up.Dst!, lo.Org!))
			{
				// up.Dst is on the left. It is safe to form triangles from lo.Org. The EdgeGoesLeft test guarantees
				// progress even when some triangles are CW, given that the upper and lower chains are truly monotone.
				while (lo.Lnext != up && (EdgeGoesLeft(lo.Lnext) || EdgeSign(lo.Org!, lo.Dst!, lo.Lnext.Dst!) <= 0))
					lo = MeshConnect(lo.Lnext, lo).Sym;
				lo = lo.Lprev;
			}
			else
			{
				// lo.Org is on the left. We can make CCW triangles from up.Dst.
				while (lo.Lnext != up && (EdgeGoesRight(up.Lprev) || EdgeSign(up.Dst!, up.Org!, up.Lprev.Org!) >= 0))
					up = MeshConnect(up, up.Lprev).Sym;
				up = up.Lnext;
			}
		}

		// Now lo.Org == up.Dst == the leftmost vertex. The remaining region can be tessellated in a fan from it.
		Debug.Assert(lo.Lnext != up);
		while (lo.Lnext.Lnext != up)
			lo = MeshConnect(lo.Lnext, lo).Sym;
	}

	// Tessellate each region of the mesh which is marked "inside" the polygon. Each such region must be monotone.
	private void MeshTessellateInterior()
	{
		Face next;
		for (var f = _fHead.Next; f != _fHead; f = next)
		{
			// Make sure we don't try to tessellate the new triangles.
			next = f.Next;
			if (f.Inside)
				MeshTessellateMonoRegion(f);
		}
	}

	/*
	* Reset the winding numbers on all edges so that regions marked "inside" the polygon have a winding number of 1, and
	* regions outside have a winding number of 0, deleting every edge which does not separate an interior region from an
	* exterior one.
	*/
	private void MeshKeepOnlyBoundary()
	{
		HalfEdge eNext;
		for (var e = _eHead.Next; e != _eHead; e = eNext)
		{
			eNext = e.Next;
			if (e.Rface!.Inside != e.Lface!.Inside)
				e.Winding = e.Lface.Inside ? 1 : -1; // a boundary edge
			else
				MeshDelete(e);
		}
	}
}
