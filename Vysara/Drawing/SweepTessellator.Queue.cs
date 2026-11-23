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
	/// <summary>
	/// The sweep's event queue: the vertices in VertLeq order, with insertion of new (intersection) vertices and deletion
	/// by handle. The initial vertices are sorted once into an array consumed from its end; vertices inserted later go
	/// into a heap. Extraction takes the smaller of the two minimums. Handles below zero name sorted-array slots, others
	/// heap handles. Its arrays are retained and grow by doubling.
	/// </summary>
	private sealed class PriorityQueue
	{
		// The heap: active nodes in 1..heapSize, children of node i at 2i and 2i+1. Each node holds a handle; each handle
		// a key and the node that currently represents it.
		private int[] _nodes = new int[33];
		private Vertex?[] _handleKeys = new Vertex?[33];
		private int[] _handleNodes = new int[33];
		private int _heapSize, _heapMax;
		private int _freeList;
		private bool _heapInitialized;

		// The sorted array of the initial keys, and their order, largest first.
		private Vertex?[] _keys = new Vertex?[32];
		private int[] _order = new int[33];
		private int _size, _max;
		private bool _initialized;

		private readonly (int P, int R)[] _stack = new (int, int)[50];

		public void Reset(int size)
		{
			_heapSize = 0;
			_heapMax = size;
			EnsureHeap(size);
			_heapInitialized = false;
			_freeList = 0;
			_nodes[1] = 1; // so that the minimum of an empty heap is null
			_handleKeys[1] = null;

			if (_keys.Length < size)
				_keys = new Vertex?[size];
			Array.Clear(_keys);
			_size = 0;
			_max = size;
			_initialized = false;
		}

		private void EnsureHeap(int max)
		{
			if (_nodes.Length >= max + 1)
				return;
			var length = Math.Max(max + 1, _nodes.Length * 2);
			Array.Resize(ref _nodes, length);
			Array.Resize(ref _handleKeys, length);
			Array.Resize(ref _handleNodes, length);
		}

		private void FloatDown(int curr)
		{
			var hCurr = _nodes[curr];
			for (; ; )
			{
				var child = curr << 1;
				if (child < _heapSize && VertLeq(_handleKeys[_nodes[child + 1]]!, _handleKeys[_nodes[child]]!))
					++child;

				var hChild = _nodes[child];
				if (child > _heapSize || VertLeq(_handleKeys[hCurr]!, _handleKeys[hChild]!))
				{
					_nodes[curr] = hCurr;
					_handleNodes[hCurr] = curr;
					break;
				}
				_nodes[curr] = hChild;
				_handleNodes[hChild] = curr;
				curr = child;
			}
		}

		private void FloatUp(int curr)
		{
			var hCurr = _nodes[curr];
			for (; ; )
			{
				var parent = curr >> 1;
				var hParent = _nodes[parent];
				if (parent == 0 || VertLeq(_handleKeys[hParent]!, _handleKeys[hCurr]!))
				{
					_nodes[curr] = hCurr;
					_handleNodes[hCurr] = curr;
					break;
				}
				_nodes[curr] = hParent;
				_handleNodes[hParent] = curr;
				curr = parent;
			}
		}

		private void HeapInit()
		{
			// This way of building a heap is O(n), rather than O(n lg n).
			for (var i = _heapSize; i >= 1; --i)
				FloatDown(i);
			_heapInitialized = true;
		}

		private int HeapInsert(Vertex keyNew)
		{
			var curr = ++_heapSize;
			if (curr * 2 > _heapMax)
			{
				// If the heap overflows, double its size.
				_heapMax <<= 1;
				EnsureHeap(_heapMax);
			}

			int free;
			if (_freeList == 0)
				free = curr;
			else
			{
				free = _freeList;
				_freeList = _handleNodes[free];
			}

			_nodes[curr] = free;
			_handleNodes[free] = curr;
			_handleKeys[free] = keyNew;

			if (_heapInitialized)
				FloatUp(curr);
			return free;
		}

		private Vertex? HeapMinimum => _handleKeys[_nodes[1]];

		private bool HeapIsEmpty => _heapSize == 0;

		private Vertex? HeapExtractMin()
		{
			var hMin = _nodes[1];
			var min = _handleKeys[hMin];

			if (_heapSize > 0)
			{
				_nodes[1] = _nodes[_heapSize];
				_handleNodes[_nodes[1]] = 1;

				_handleKeys[hMin] = null;
				_handleNodes[hMin] = _freeList;
				_freeList = hMin;

				if (--_heapSize > 0)
					FloatDown(1);
			}
			return min;
		}

		private void HeapDelete(int hCurr)
		{
			var curr = _handleNodes[hCurr];
			_nodes[curr] = _nodes[_heapSize];
			_handleNodes[_nodes[curr]] = curr;

			if (curr <= --_heapSize)
			{
				if (curr <= 1 || VertLeq(_handleKeys[_nodes[curr >> 1]]!, _handleKeys[_nodes[curr]]!))
					FloatDown(curr);
				else
					FloatUp(curr);
			}
			_handleKeys[hCurr] = null;
			_handleNodes[hCurr] = _freeList;
			_freeList = hCurr;
		}

		private bool Lt(int x, int y) => !VertLeq(_keys[y]!, _keys[x]!);

		private bool Gt(int x, int y) => !VertLeq(_keys[x]!, _keys[y]!);

		/// <summary>Sort the keys inserted so far (descending, by randomized quicksort) and start the heap.</summary>
		public void Init()
		{
			if (_order.Length < _size + 1)
				_order = new int[Math.Max(_size + 1, _order.Length * 2)];
			for (var k = 0; k < _size; k++)
				_order[k] = k;

			var seed = 2016473283u;
			var top = 0;
			_stack[top++] = (0, _size - 1);
			while (--top >= 0)
			{
				var (p, r) = _stack[top];
				while (r > p + 10)
				{
					seed = seed * 1539415821 + 1;
					var i = p + (int)(seed % (uint)(r - p + 1));
					var piv = _order[i];
					_order[i] = _order[p];
					_order[p] = piv;
					i = p - 1;
					var j = r + 1;
					do
					{
						do { ++i; } while (Gt(_order[i], piv));
						do { --j; } while (Lt(_order[j], piv));
						(_order[i], _order[j]) = (_order[j], _order[i]);
					} while (i < j);
					(_order[i], _order[j]) = (_order[j], _order[i]); // undo the last swap
					if (i - p < r - j)
					{
						_stack[top++] = (j + 1, r);
						r = i - 1;
					}
					else
					{
						_stack[top++] = (p, i - 1);
						p = j + 1;
					}
				}
				// Insertion sort small lists
				for (var i = p + 1; i <= r; ++i)
				{
					var piv = _order[i];
					int j;
					for (j = i; j > p && Lt(_order[j - 1], piv); --j)
						_order[j] = _order[j - 1];
					_order[j] = piv;
				}
			}
			_max = _size;
			_initialized = true;
			HeapInit();
		}

		/// <summary>Insert a key, returning its handle: a sorted-array slot (negative) before <see cref="Init"/>, a heap handle
		/// after.</summary>
		public int Insert(Vertex keyNew)
		{
			if (_initialized)
				return HeapInsert(keyNew);
			var curr = _size;
			if (++_size >= _max)
			{
				_max <<= 1;
				Array.Resize(ref _keys, _max);
			}
			_keys[curr] = keyNew;
			return -(curr + 1);
		}

		public Vertex? ExtractMin()
		{
			if (_size == 0)
				return HeapExtractMin();
			var sortMin = _keys[_order[_size - 1]];
			if (!HeapIsEmpty)
			{
				var heapMin = HeapMinimum;
				if (VertLeq(heapMin!, sortMin!))
					return HeapExtractMin();
			}
			do
			{
				--_size;
			} while (_size > 0 && _keys[_order[_size - 1]] == null);
			return sortMin;
		}

		public Vertex? Minimum()
		{
			if (_size == 0)
				return HeapMinimum;
			var sortMin = _keys[_order[_size - 1]];
			if (!HeapIsEmpty)
			{
				var heapMin = HeapMinimum;
				if (VertLeq(heapMin!, sortMin!))
					return heapMin;
			}
			return sortMin;
		}

		public void Delete(int curr)
		{
			if (curr >= 0)
			{
				HeapDelete(curr);
				return;
			}
			curr = -(curr + 1);
			_keys[curr] = null;
			while (_size > 0 && _keys[_order[_size - 1]] == null)
				--_size;
		}

		/// <summary>Drop every key, so pooled vertices are not kept alive.</summary>
		public void Clear()
		{
			Array.Clear(_keys);
			Array.Clear(_handleKeys);
			_size = _heapSize = 0;
		}
	}
}
