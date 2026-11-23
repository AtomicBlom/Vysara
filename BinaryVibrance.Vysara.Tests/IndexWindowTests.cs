using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Rasterizer;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Draw-list indices are 16-bit. A surface with more vertices than one index can address is split into index
/// windows: when a quad would not fit, the open run is flushed at that quad and continues in a new command whose
/// <see cref="DrawCommand.VertexOffset"/> is the window's first vertex, with indices relative to it. These tests pin
/// where the split falls and prove, in pixels, that a split surface draws exactly what the same content draws when
/// spread over surfaces small enough never to split.
/// </summary>
public sealed class IndexWindowTests
{
	// 2x2 quads stepped 1px apart in a 150-column grid, so every quad overlaps its neighbours and draw order shows in
	// the pixels. 22,500 quads are 90,000 vertices, well past one window of 65,535.
	private const int QuadCount = 22_500;
	private const int Columns = 150;
	private const int Size = Columns + 2;
	private const int QuadsPerWindow = UIDrawList.MaxWindowVertices / 4;

	[Test]
	public void A_run_past_the_16_bit_index_range_splits_into_a_new_window_at_a_quad_boundary()
	{
		var root = new RootVisual { SurfaceSize = new Vector2(Size, Size) };
		for (var i = 0; i < QuadCount; i++)
			root.Add(Quad(i));
		var draw = Composite(root);

		var vertices = draw.Vertices.Span;
		var indices = draw.Indices.Span;
		var commands = draw.Commands.Span;
		vertices.Length.ShouldBe(QuadCount * 4);
		indices.Length.ShouldBe(QuadCount * 6);

		// One state run, split once: the first window takes the 16,383 quads that fit, and the rest start a second
		// window at the next quad's first vertex.
		commands.Length.ShouldBe(2);
		commands[0].ShouldBe(new DrawCommand(new Rect(0, 0, Size, Size), 0, 0, QuadsPerWindow * 6, 0, (uint)UIPrimKind.Fill));
		commands[1].ShouldBe(new DrawCommand(new Rect(0, 0, Size, Size), 0, QuadsPerWindow * 6, (QuadCount - QuadsPerWindow) * 6,
			QuadsPerWindow * 4, (uint)UIPrimKind.Fill));

		// Every index stays inside its window, never reaches the primitive-restart value, and resolves to the vertex the
		// unsplit stream would have used: quad q's corners are vertices 4q..4q+3.
		foreach (var cmd in commands)
		{
			for (var i = cmd.IndexOffset; i < cmd.IndexOffset + cmd.IndexCount; i++)
				indices[i].ShouldBeLessThan((ushort)UIDrawList.MaxWindowVertices);
		}
		var firstOfSecondWindow = commands[1].VertexOffset + indices[commands[1].IndexOffset];
		firstOfSecondWindow.ShouldBe(QuadsPerWindow * 4);
		var lastVertex = commands[1].VertexOffset + indices[^1];
		lastVertex.ShouldBe(vertices.Length - 1);
	}

	[Test]
	public void A_split_surface_with_a_layer_rasterizes_like_the_same_content_in_smaller_surfaces() =>
		AssertSplitMatchesSmallSurfaces(withLayer: true, parallel: false);

	[Test]
	public void A_split_surface_rasterizes_like_the_same_content_in_smaller_surfaces_in_parallel_bands() =>
		AssertSplitMatchesSmallSurfaces(withLayer: false, parallel: true);

	// Composite the quads (and optionally a faded group after them, whose layer marker then sits past vertex 65,535)
	// into one surface, and the same visuals into three surfaces of 7,500 quads each; render both onto a white
	// framebuffer, the three in order, and require identical bytes.
	private static void AssertSplitMatchesSmallSurfaces(bool withLayer, bool parallel)
	{
		var whole = new RootVisual { SurfaceSize = new Vector2(Size, Size) };
		for (var i = 0; i < QuadCount; i++)
			whole.Add(Quad(i));
		if (withLayer)
			whole.Add(FadedGroup());
		var compositor = new DrawListCompositor();
		compositor.Composite(whole, new DrawListId(0));
		var split = compositor.LastDraw(new DrawListId(0));
		split.Vertices.Length.ShouldBeGreaterThan(UIDrawList.MaxWindowVertices);
		split.Commands.ToArray().ShouldContain(c => c.VertexOffset > 0 && c.Tag == (uint)UIPrimKind.Fill);

		const int chunks = 3;
		const int perChunk = QuadCount / chunks;
		var small = new DrawListData[chunks];
		for (var k = 0; k < chunks; k++)
		{
			var root = new RootVisual { SurfaceSize = new Vector2(Size, Size) };
			for (var i = k * perChunk; i < (k + 1) * perChunk; i++)
				root.Add(Quad(i));
			if (withLayer && k == chunks - 1)
				root.Add(FadedGroup());
			compositor.Composite(root, new DrawListId(1 + k));
			small[k] = compositor.LastDraw(new DrawListId(1 + k));
			small[k].Vertices.Length.ShouldBeLessThan(UIDrawList.MaxWindowVertices);
		}

		if (withLayer)
		{
			// The layer marker names its tint vertex by absolute index, past the 16-bit range.
			var marker = split.Commands.ToArray().Single(c => c.Tag == (uint)UIPrimKind.BeginLayer);
			marker.VertexOffset.ShouldBeGreaterThan(UIDrawList.MaxWindowVertices);
			((float)split.Vertices.Span[marker.VertexOffset].Color.A).ShouldBe(0.5f);
		}

		var expected = Rasterize(parallel, small, out _);
		var actual = Rasterize(parallel, [split], out var layers);

		DifferingPixels(actual, expected).ShouldBe(0);
		DistinctColours(actual).ShouldBeGreaterThan(1000); // a real picture, not two equally blank frames
		layers.ShouldBe(withLayer ? 1 : 0);
	}

	// Each quad gets its own colour (red and green spell out its index), so a vertex fetched from the wrong quad shows.
	private static SpriteVisual Quad(int i) => new()
	{
		Transform = Matrix3x2.CreateTranslation(i % Columns, i / Columns),
		Content = new RectContent(new Vector2(2, 2), new CompositionColorBrush(HdrColor.FromRgb((byte)i, (byte)(i >> 8), (byte)(i * 37)))),
	};

	// Two overlapping opaque rects under a half-opacity container: a group the compositor draws as one layer.
	private static ContainerVisual FadedGroup()
	{
		var group = new ContainerVisual { Opacity = 0.5f };
		group.Add(new SpriteVisual
		{
			Transform = Matrix3x2.CreateTranslation(20, 20),
			Content = new RectContent(new Vector2(60, 60), new CompositionColorBrush(HdrColor.FromRgb(255, 255, 255))),
		});
		group.Add(new SpriteVisual
		{
			Transform = Matrix3x2.CreateTranslation(50, 50),
			Content = new RectContent(new Vector2(60, 60), new CompositionColorBrush(HdrColor.FromRgb(0, 0, 0))),
		});
		return group;
	}

	private static DrawListData Composite(RootVisual root)
	{
		var compositor = new DrawListCompositor();
		compositor.Composite(root, new DrawListId(0));
		return compositor.LastDraw(new DrawListId(0));
	}

	private static byte[] Rasterize(bool parallel, DrawListData[] lists, out int layers)
	{
		var white = HdrColor.FromRgb(255, 255, 255).ToPremultiplied().ToVector4();
		var rast = new SoftwareRasterizer(Size, Size);
		rast.Clear(white);
		layers = 0;
		foreach (var list in lists)
		{
			if (parallel)
				rast.RenderParallel(list, clearColor: null);
			else
				rast.Render(list);
			layers += rast.LayersComposited;
		}
		return rast.ResolveToRgba8();
	}

	private static int DifferingPixels(byte[] a, byte[] b)
	{
		var differing = 0;
		for (var i = 0; i < a.Length; i += 4)
		{
			if (!a.AsSpan(i, 4).SequenceEqual(b.AsSpan(i, 4)))
				differing++;
		}
		return differing;
	}

	private static int DistinctColours(byte[] rgba)
	{
		var colours = new HashSet<int>();
		for (var i = 0; i < rgba.Length; i += 4)
			colours.Add(BitConverter.ToInt32(rgba, i));
		return colours.Count;
	}
}
