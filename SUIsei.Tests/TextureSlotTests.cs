using System.Numerics;

using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Rasterizer;

namespace SUIsei.Tests;

/// <summary>
/// Per-list texture slots: each draw list carries its own <see cref="DrawListData.TextureSlots"/>, with the white
/// texel in slot 0 and every other source it samples (glyph atlas pages, images) in a dense slot numbered by first use,
/// and a <see cref="DrawListData.TextureRevision"/> that moves only when the textures do. The rasterizer binds through
/// the slot table, the way a host does.
/// </summary>
public sealed class TextureSlotTests
{
	private const int W = 120, H = 40;

	// A solid-colour image whose bytes the test keeps, so it can rewrite them.
	private static (ImageSource Source, byte[] Rgba) Solid(byte r, byte g, byte b)
	{
		var rgba = new byte[4 * 4 * 4];
		for (var i = 0; i < rgba.Length; i += 4)
		{
			rgba[i] = r;
			rgba[i + 1] = g;
			rgba[i + 2] = b;
			rgba[i + 3] = 255;
		}
		return (ImageSource.FromRgba8(4, 4, rgba), rgba);
	}

	// A surface with a text block and two images: text at x 0..40, image a at x 40..80, image b at x 80..120.
	private sealed class Scene
	{
		public readonly UIRoot Root = new(new DrawListId(0));
		public readonly TextBlock Text = new() { Text = "Hi", FontSize = 16f, Width = 40f, Height = 40f };
		public readonly Image A;
		public readonly Image B;
		public readonly ImageSource SourceA;
		public readonly ImageSource SourceB;
		public readonly byte[] BytesA;

		public Scene()
		{
			(SourceA, BytesA) = Solid(255, 0, 0);
			(SourceB, _) = Solid(0, 0, 255);
			A = new Image { Source = SourceA, Width = 40f, Height = 40f, Stretch = Stretch.Fill };
			B = new Image { Source = SourceB, Width = 40f, Height = 40f, Stretch = Stretch.Fill };
			var canvas = new Canvas();
			canvas.Children.Add(Text);
			canvas.Children.Add(A);
			canvas.Children.Add(B);
			Canvas.SetLeft(A, 40f);
			Canvas.SetLeft(B, 80f);
			Root.Content = canvas;
		}

		public DrawListData Frame()
		{
			Root.LayoutPass(new Size(W, H));
			return Root.Render();
		}
	}

	private static int SlotOf(DrawListData draw, UIPrimKind kind) =>
		draw.Commands.ToArray().First(c => (UIPrimKind)c.Tag == kind).TextureIndex;

	[Test]
	public void Slots_are_dense_numbered_by_first_use_and_each_source_appears_once()
	{
		var scene = new Scene();
		var canvas = (Canvas)scene.Root.Content!;
		canvas.Children.Add(new Image { Source = scene.SourceA, Width = 10f, Height = 10f, Stretch = Stretch.Fill }); // source A again: no new slot
		canvas.Children.Add(new Border { Width = 5f, Height = 5f, Background = HdrColor.FromRgb(9, 9, 9) });          // an untextured fill

		var draw = scene.Frame();

		draw.TextureSlots.Count.ShouldBe(4);
		draw.TextureSlots[0].ShouldBeSameAs(TextureSource.White);
		draw.TextureSlots[1].ShouldBeOfType<AtlasImage>();   // the text came first in the walk
		draw.TextureSlots[2].ShouldBeSameAs(scene.SourceA);
		draw.TextureSlots[3].ShouldBeSameAs(scene.SourceB);
		SlotOf(draw, UIPrimKind.Glyph).ShouldBe(1);
		draw.Commands.ToArray().Where(c => (UIPrimKind)c.Tag == UIPrimKind.Image).Select(c => c.TextureIndex).ShouldBe([2, 3, 2]);
		var fills = draw.Commands.ToArray().Where(c => (UIPrimKind)c.Tag == UIPrimKind.Fill).ToArray();
		fills.ShouldNotBeEmpty();
		fills.ShouldAllBe(c => c.TextureIndex == 0);   // untextured commands sample the white texel
	}

	[Test]
	public void Every_commands_texture_index_is_within_its_lists_slot_table()
	{
		var scene = new Scene();
		var faded = new Border { Opacity = 0.5f, Child = new Border { Width = 5f, Height = 5f, Background = HdrColor.FromRgb(9, 9, 9) } };
		((Canvas)scene.Root.Content!).Children.Add(faded);   // a group-opacity layer: its markers carry slot 0

		var draw = scene.Frame();
		var commands = draw.Commands.ToArray();

		commands.ShouldContain(c => (UIPrimKind)c.Tag == UIPrimKind.BeginLayer);
		commands.ShouldAllBe(c => c.TextureIndex >= 0 && c.TextureIndex < draw.TextureSlots.Count);
	}

	[Test]
	public void A_geometry_only_recomposite_keeps_the_slots_and_the_texture_revision()
	{
		var scene = new Scene();
		var first = scene.Frame();
		var slots = first.TextureSlots.ToArray();
		var revision = first.Revision;
		first.TextureRevision.ShouldBe(1u);

		Canvas.SetTop(scene.A, 5f);   // moves geometry only
		var second = scene.Frame();

		second.Revision.ShouldBe(revision + 1);
		second.TextureSlots.ToArray().ShouldBe(slots);   // the same sources in the same slots
		second.TextureRevision.ShouldBe(1u);
	}

	[Test]
	public void A_different_slot_table_moves_the_texture_revision()
	{
		var scene = new Scene();
		scene.Frame().TextureRevision.ShouldBe(1u);

		((Canvas)scene.Root.Content!).Children.RemoveAt(2);   // image b goes, so source B is no longer sampled
		var draw = scene.Frame();

		draw.TextureSlots.Count.ShouldBe(3);
		draw.TextureRevision.ShouldBe(2u);
	}

	[Test]
	public void A_source_whose_texels_change_moves_the_texture_revision_even_on_a_clean_frame()
	{
		var scene = new Scene();
		var draw = scene.Frame();
		var revision = draw.Revision;

		scene.SourceA.InvalidateTexels();
		var again = scene.Frame();   // nothing in the tree is dirty: the same list is re-submitted

		again.ShouldBeSameAs(draw);
		again.Revision.ShouldBe(revision);
		again.TextureRevision.ShouldBe(2u);
		again.Dump().ShouldContain("ImageSource 4x4 v1");

		scene.Frame().TextureRevision.ShouldBe(2u);   // and it doesn't move again without another change

		Canvas.SetTop(scene.A, 3f);   // the next real recomposite carries the new version forward unchanged
		scene.Frame().TextureRevision.ShouldBe(2u);
	}

	[Test]
	public void The_rasterizer_binds_through_the_slot_table_and_follows_texel_changes()
	{
		var scene = new Scene();
		var rasterizer = new SoftwareRasterizer(W, H);

		byte[] Render()
		{
			var draw = scene.Frame();
			rasterizer.Clear(Vector4.Zero);
			rasterizer.Render(draw);
			rasterizer.SkippedCommands.ShouldBe(0);
			return rasterizer.ResolveToRgba8();
		}

		static (byte R, byte G, byte B) At(byte[] px, int x, int y)
		{
			var o = (y * W + x) * 4;
			return (px[o], px[o + 1], px[o + 2]);
		}

		var pixels = Render();
		At(pixels, 60, 20).ShouldBe(((byte)255, (byte)0, (byte)0));   // image a samples its own slot
		At(pixels, 100, 20).ShouldBe(((byte)0, (byte)0, (byte)255));  // image b its own
																	  // The glyphs sampled the atlas page in their slot: something drew over the clear background in the text's box.
		Enumerable.Range(0, 40 * 40).Any(i => At(pixels, i % 40, i / 40) != ((byte)0, (byte)0, (byte)0)).ShouldBeTrue();

		// Rewrite image a's texels: the rasterizer re-copies a source when its version moves, as a host re-uploads.
		for (var i = 0; i < scene.BytesA.Length; i += 4)
		{
			scene.BytesA[i] = 0;
			scene.BytesA[i + 1] = 255;
		}
		scene.SourceA.InvalidateTexels();
		At(Render(), 60, 20).ShouldBe(((byte)0, (byte)255, (byte)0));
	}

	[Test]
	public void A_list_built_by_hand_with_no_slot_for_a_textured_command_skips_it()
	{
		var draw = new DrawListData
		{
			Vertices = new OverlayVertex[]
			{
				new(new Vector2(0, 0), new Vector2(0, 0), default, Vector4.Zero),
				new(new Vector2(10, 0), new Vector2(1, 0), default, Vector4.Zero),
				new(new Vector2(10, 10), new Vector2(1, 1), default, Vector4.Zero),
			},
			Indices = new ushort[] { 0, 1, 2 },
			Commands = new DrawCommand[] { new(new Rect(0, 0, 10, 10), 3, 0, 3, 0, (uint)UIPrimKind.Image) },
			TextureSlots = [TextureSource.White],
		};
		var rasterizer = new SoftwareRasterizer(16, 16);

		rasterizer.Render(draw);

		rasterizer.SkippedCommands.ShouldBe(1);
	}
}
