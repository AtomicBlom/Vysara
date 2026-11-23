using System.Numerics;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Rasterizer;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Group layers sized to what they draw: a layer's rect is the bounds of its content rounded out to whole pixels and
/// clipped, not the clip it inherits, so a small faded group costs a small target; an empty layer is left out; and the
/// CPU oracle clips a layer's content to its rect, as a backend's render area does, and pools its layer framebuffers.
/// </summary>
public sealed class LayerBoundsTests
{
	private const int Size = 200;

	private static (StackPanel Group, UIRoot Root) FadedGroup(bool drawsSomething = true)
	{
		var group = new StackPanel { Opacity = 0.5f };
		group.Children.Add(new Border { Width = 10, Height = 10, Background = drawsSomething ? new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) : null });
		group.Children.Add(new Border { Width = 10, Height = 10, Background = drawsSomething ? new SolidColorBrush(HdrColor.FromRgb(0, 0, 255)) : null });
		var canvas = new Canvas();
		canvas.Children.Add(group);
		Canvas.SetLeft(group, 60.25f);
		Canvas.SetTop(group, 70);
		var root = new UIRoot(new DrawListId(0)) { Content = canvas };
		root.LayoutPass(new Size(Size, Size));
		return (group, root);
	}

	private static DrawCommand Begin(DrawListData draw) =>
		draw.Commands.ToArray().Single(c => c.Tag == (uint)UIPrimKind.BeginLayer);

	[Test]
	public void A_faded_groups_layer_is_the_bounds_of_what_it_draws_not_the_inherited_clip()
	{
		var draw = FadedGroup().Root.Render();
		var begin = Begin(draw);

		// The children span x 60.25..70.25 and y 70..90; rounded out to whole pixels.
		begin.ClipRect.ShouldBe(new Rect(60, 70, 11, 20));
		var marker = draw.Vertices.Span.Slice(begin.VertexOffset, 4).ToArray().Select(v => v.Position).ToArray();
		marker.ShouldBe([new Vector2(60, 70), new Vector2(71, 70), new Vector2(71, 90), new Vector2(60, 90)]);
	}

	[Test]
	public void The_narrowed_layer_draws_the_same_pixels_as_a_surface_wide_one()
	{
		var draw = FadedGroup().Root.Render();
		var vertices = draw.Vertices.ToArray();
		var commands = draw.Commands.ToArray();
		var b = Array.FindIndex(commands, c => c.Tag == (uint)UIPrimKind.BeginLayer);
		var whole = new Rect(0, 0, Size, Size);
		commands[b] = commands[b] with { ClipRect = whole };
		Vector2[] corners = [new(0, 0), new(Size, 0), new(Size, Size), new(0, Size)];
		for (var i = 0; i < 4; i++)
			vertices[commands[b].VertexOffset + i] = vertices[commands[b].VertexOffset + i] with { Position = corners[i] };
		var wide = new DrawListData
		{
			Vertices = vertices,
			Indices = draw.Indices.ToArray(),
			Commands = commands,
			TextureSlots = draw.TextureSlots.ToArray(),
			CoordinateSize = draw.CoordinateSize,
		};

		Render(draw).ShouldBe(Render(wide));
	}

	[Test]
	public void A_layer_with_nothing_drawn_in_it_is_left_out()
	{
		var draw = FadedGroup(drawsSomething: false).Root.Render();
		draw.Commands.ToArray().ShouldNotContain(c => c.Tag == (uint)UIPrimKind.BeginLayer || c.Tag == (uint)UIPrimKind.EndLayer);
		draw.Vertices.Length.ShouldBe(0);
	}

	[Test]
	public void A_nested_layer_grows_its_parent_by_its_composite()
	{
		var fill = new TessellatedPath();
		new Tessellator().Fill(Geometry.Parse("M 10,10 L 20,10 20,20 10,20 Z").CompositionPath, TessellationScale.Identity, fill);
		var whole = new Rect(0, 0, Size, Size);
		var builder = new DrawListBuilder();
		builder.Begin(new Vector2(Size));
		builder.BeginLayer(whole, 0.5f);
		builder.BeginLayer(whole, 0.5f);
		builder.AddVector(fill, Matrix3x2.Identity, PremultipliedHdrColor.FromStraight(1, 1, 1, 1), whole);
		builder.EndLayer(whole);
		builder.EndLayer(whole);
		var draw = builder.End();

		var begins = draw.Commands.ToArray().Where(c => c.Tag == (uint)UIPrimKind.BeginLayer).Select(c => c.ClipRect).ToArray();
		begins.ShouldBe([new Rect(9, 9, 12, 12), new Rect(9, 9, 12, 12)]); // the fill's fringe reaches half a pixel out
	}

	[Test]
	public void The_rasterizer_clips_a_layers_content_to_its_rect()
	{
		var white = PremultipliedHdrColor.FromStraight(1, 1, 1, 1);
		var tint = PremultipliedHdrColor.FromStraight(1, 1, 1, 1);
		var draw = new DrawListData
		{
			Vertices = new OverlayVertex[]
			{
				new(new Vector2(0, 0), Vector2.Zero, tint, Vector4.Zero),
				new(new Vector2(10, 0), Vector2.Zero, tint, Vector4.Zero),
				new(new Vector2(10, 10), Vector2.Zero, tint, Vector4.Zero),
				new(new Vector2(0, 10), Vector2.Zero, tint, Vector4.Zero),
				new(new Vector2(0, 0), Vector2.Zero, white, Vector4.Zero),
				new(new Vector2(20, 0), Vector2.Zero, white, Vector4.Zero),
				new(new Vector2(20, 20), Vector2.Zero, white, Vector4.Zero),
				new(new Vector2(0, 20), Vector2.Zero, white, Vector4.Zero),
			},
			Indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
			Commands = new DrawCommand[]
			{
				new(new Rect(0, 0, 10, 10), 0, 0, 0, 0, (uint)UIPrimKind.BeginLayer),
				new(new Rect(0, 0, 20, 20), 0, 0, 6, 4, (uint)UIPrimKind.Fill),
				new(new Rect(0, 0, 20, 20), 0, 6, 0, 0, (uint)UIPrimKind.EndLayer),
			},
			CoordinateSize = new Vector2(20, 20),
		};
		var raster = new SoftwareRasterizer(20, 20);
		raster.Render(draw);
		var px = raster.ResolveToRgba8();

		px[(5 * 20 + 5) * 4 + 3].ShouldBe((byte)255);
		px[(15 * 20 + 15) * 4 + 3].ShouldBe((byte)0);
		px[(5 * 20 + 15) * 4 + 3].ShouldBe((byte)0);
	}

	[Test]
	public void The_rasterizer_pools_its_layer_framebuffers()
	{
		var draw = FadedGroup().Root.Render();
		var raster = new SoftwareRasterizer(Size, Size);
		raster.Render(draw);
		raster.Render(draw);

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 5; i++)
				raster.Render(draw);
		});
		raster.LayersComposited.ShouldBe(1);
	}

	private static byte[] Render(DrawListData draw)
	{
		var raster = new SoftwareRasterizer(Size, Size);
		raster.Clear(new Vector4(0.1f, 0.2f, 0.3f, 1f));
		raster.Render(draw);
		return raster.ResolveToRgba8();
	}
}
