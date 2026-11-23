using System.Numerics;
using Vysara.Drawing;
using Vysara.Rasterizer;

namespace Vysara.Tests;

/// <summary>
/// The software rasterizer is the oracle the GPU backends are diffed against, so a command tag it has no shading for
/// must fail the render rather than be drawn as a solid fill that a test would then accept.
/// </summary>
public sealed class RasterizerKindTests
{
	private const int Size = 8;

	[Test]
	[Arguments(99u)]
	[Arguments(256u)] // would alias to Fill if the tag were narrowed to the byte-sized UIPrimKind first
	[Arguments(uint.MaxValue)]
	public void An_unknown_primitive_kind_throws_before_anything_is_drawn(uint tag)
	{
		// A valid fill quad, then the same quad under the unknown tag.
		var red = PremultipliedHdrColor.FromStraight(1f, 0f, 0f, 1f);
		var draw = new DrawListData
		{
			Vertices = new OverlayVertex[]
			{
				new(new Vector2(0, 0), new Vector2(0, 0), red, Vector4.Zero),
				new(new Vector2(Size, 0), new Vector2(1, 0), red, Vector4.Zero),
				new(new Vector2(Size, Size), new Vector2(1, 1), red, Vector4.Zero),
				new(new Vector2(0, Size), new Vector2(0, 1), red, Vector4.Zero),
			},
			Indices = new ushort[] { 0, 1, 2, 0, 2, 3, 0, 1, 2, 0, 2, 3 },
			Commands = new DrawCommand[]
			{
				new(new Rect(0, 0, Size, Size), 0, 0, 6, 0, (uint)UIPrimKind.Fill),
				new(new Rect(0, 0, Size, Size), 0, 6, 6, 0, tag),
			},
			CoordinateSize = new Vector2(Size, Size),
		};
		var white = HdrColor.FromRgb(255, 255, 255).ToPremultiplied().ToVector4();
		var rast = new SoftwareRasterizer(Size, Size);

		rast.Clear(white);
		Should.Throw<ArgumentException>(() => rast.Render(draw));
		Should.Throw<ArgumentException>(() => rast.RenderParallel(draw, clearColor: null));

		// The tags are checked before any command draws, so the valid fill never reached the framebuffer.
		rast.ResolveToRgba8().ShouldAllBe(b => b == 255);
	}
}
