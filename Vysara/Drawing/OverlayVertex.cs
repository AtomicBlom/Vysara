using System.Numerics;
using System.Runtime.InteropServices;

namespace Vysara.Drawing;

/// <summary>
/// The vertex of a 2D overlay draw list — a pixel-space position, texture coordinate, premultiplied colour, and
/// a float4 of per-vertex data the consuming technique defines. Blittable and sequentially laid out so it can be
/// uploaded directly to a GPU vertex buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct OverlayVertex(Vector2 Position, Vector2 Uv, PremultipliedHdrColor Color, Vector4 Custom);
