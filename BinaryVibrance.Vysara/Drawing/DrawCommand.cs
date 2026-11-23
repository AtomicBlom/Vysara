using System.Runtime.InteropServices;
using BinaryVibrance.Vysara;

namespace BinaryVibrance.Vysara.Drawing;

/// <summary>
/// One batched draw call in a draw list: a contiguous slice of the index stream drawn under a scissor rectangle
/// with one texture bound. Blittable and sequentially laid out — the engine glue can reinterpret its memory as the
/// equivalent engine draw command without copying.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct DrawCommand(
	Rect ClipRect,
	int TextureIndex,
	int IndexOffset,
	int IndexCount,
	int VertexOffset,
	uint Tag);
