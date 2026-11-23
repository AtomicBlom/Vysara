using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Glyph-run composition coverage: a <see cref="TextBlock"/> drives the element→visual pipeline through the default
/// <see cref="DrawListCompositor"/> (which lays out against the embedded <see cref="FontAtlas.Default"/>) into a
/// neutral <see cref="DrawListData"/>. Asserts the emitted draw list on CPU — glyph kind, atlas texture binding,
/// quad/index counts, batching, the screenPxRange ride-along — with no backend.
/// </summary>
public sealed class GlyphCompositionTests
{
	private static UIRoot Root(UIElement content, float w = 400f, float h = 100f)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(w, h));
		return root;
	}

	[Test]
	public void A_text_block_emits_one_batched_glyph_command_sampling_the_atlas()
	{
		var draw = Root(new TextBlock { Text = "Hi", FontSize = 32f }).Render();

		draw.Commands.Length.ShouldBe(1);
		var cmd = draw.Commands.Span[0];
		cmd.Tag.ShouldBe((uint)UIPrimKind.Glyph);
		cmd.TextureIndex.ShouldBe(1); // the atlas page's slot: the first source after the white texel in slot 0

		// Two visible glyphs ⇒ two quads ⇒ 8 vertices / 12 indices, all collapsed into the one command.
		draw.Vertices.Length.ShouldBe(8);
		draw.Indices.Length.ShouldBe(12);
		cmd.IndexCount.ShouldBe(12);
	}

	[Test]
	public void Whitespace_advances_the_pen_but_emits_no_quad()
	{
		// "A A" = two visible glyphs around a space ⇒ two quads; the space only pushes the pen right.
		var draw = Root(new TextBlock { Text = "A A", FontSize = 32f }).Render();

		draw.Vertices.Length.ShouldBe(8);
		var firstGlyphLeft = draw.Vertices.Span[0].Position.X;   // top-left of the first 'A'
		var secondGlyphLeft = draw.Vertices.Span[4].Position.X;  // top-left of the second 'A'
		secondGlyphLeft.ShouldBeGreaterThan(firstGlyphLeft);
	}

	[Test]
	public void Empty_text_emits_nothing()
	{
		var draw = Root(new TextBlock { Text = "", FontSize = 32f }).Render();
		draw.Commands.Length.ShouldBe(0);
		draw.Vertices.Length.ShouldBe(0);
	}

	[Test]
	public void ScreenPxRange_rides_the_glyph_vertices()
	{
		var draw = Root(new TextBlock { Text = "M", FontSize = 48f }).Render();
		// Custom.x carries the per-run screenPxRange — the shader's smoothstep scale.
		draw.Vertices.Span[0].Custom.X.ShouldBe(FontAtlas.Default.ScreenPxRange(48f), 1e-2f);
	}
}
