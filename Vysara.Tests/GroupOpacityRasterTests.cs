using System.Numerics;
using Vysara.Composition;
using Vysara.Drawing;
using Vysara.Rasterizer;

namespace Vysara.Tests;

/// <summary>
/// Group-opacity coverage at the PIXEL level, headless. The <see cref="SoftwareRasterizer"/> is the CPU twin of
/// <c>ui.slang</c> (plus the RT-stack layer composite), so it proves the double-blend fix in real pixels with no
/// GPU — and is the reference the Vulkan/DX12 backends must match. The discriminating property of TRUE group
/// opacity: a uniform-colour faded group renders uniform regardless of how its children overlap. Folding the fade
/// into each child's vertex alpha (the bug) darkens the overlap.
/// </summary>
public sealed class GroupOpacityRasterTests
{
	private const int W = 100;
	private const int H = 100;

	private static (byte R, byte G, byte B, byte A) Pixel(byte[] px, int x, int y)
	{
		var o = (y * W + x) * 4;
		return (px[o], px[o + 1], px[o + 2], px[o + 3]);
	}

	private static SpriteVisual Fill(float x, float y, float w, float h, HdrColor color) => new()
	{
		Transform = Matrix3x2.CreateTranslation(x, y),
		Content = new RectContent(new Vector2(w, h), new CompositionColorBrush(color)),
	};

	private static byte[] RenderFadedRedGroup(float opacity, out SoftwareRasterizer rast)
	{
		// A 50%-opacity container holding two overlapping OPAQUE red rects, over an opaque white background.
		var group = new ContainerVisual { Opacity = opacity };
		group.Add(Fill(10, 10, 50, 50, HdrColor.FromRgb(255, 0, 0))); // covers x[10..60] y[10..60]
		group.Add(Fill(40, 40, 50, 50, HdrColor.FromRgb(255, 0, 0))); // covers x[40..90] y[40..90]; overlaps the first

		var root = new RootVisual { SurfaceSize = new Vector2(W, H) };
		root.Add(group);
		var compositor = new DrawListCompositor();
		compositor.Composite(root, new DrawListId(0));
		var draw = compositor.LastDraw(new DrawListId(0));

		rast = new SoftwareRasterizer(W, H);
		rast.Clear(HdrColor.FromRgb(255, 255, 255).ToPremultiplied().ToVector4()); // opaque white, linear premultiplied
		rast.Render(draw);
		return rast.ResolveToRgba8();
	}

	[Test]
	public void A_uniform_red_group_at_half_opacity_renders_uniform_no_double_blend()
	{
		var px = RenderFadedRedGroup(0.5f, out var rast);

		rast.LayersComposited.ShouldBe(1); // the group composited through one RT-stack layer

		var background = Pixel(px, 5, 5);    // outside the group → untouched white
		var nonOverlap = Pixel(px, 20, 20);  // only the first rect
		var overlap = Pixel(px, 50, 50);     // both rects overlap here

		background.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));

		// The overlap must match the non-overlap — uniform colour proves the fade was applied ONCE to the group,
		// not folded per-child (which would darken the overlap). The pre-fix fold gives overlap green ~140.
		Math.Abs(overlap.G - nonOverlap.G).ShouldBeLessThanOrEqualTo(2);
		Math.Abs(overlap.B - nonOverlap.B).ShouldBeLessThanOrEqualTo(2);
		overlap.G.ShouldBeGreaterThanOrEqualTo((byte)175); // true ~185; double-blended would be ~140

		// 50%-red over white in linear space resolves to sRGB ~ (255,185,185).
		nonOverlap.R.ShouldBe((byte)255);
		Math.Abs(nonOverlap.G - 185).ShouldBeLessThanOrEqualTo(3);
	}

	[Test]
	public void A_fully_opaque_group_is_unaffected_by_the_layer_path()
	{
		// Opacity 1 ⇒ no layer (NeedsLayer is false); the red rects paint straight onto white at full strength.
		var px = RenderFadedRedGroup(1f, out var rast);
		rast.LayersComposited.ShouldBe(0);
		Pixel(px, 50, 50).ShouldBe(((byte)255, (byte)0, (byte)0, (byte)255));
	}
}
