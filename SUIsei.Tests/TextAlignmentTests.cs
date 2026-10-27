using System.Numerics;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Rasterizer;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="TextBlock.TextAlignment"/> places each line of a text block across the width it is arranged at, as WinUI's
/// <c>TextAlignment</c> does: the pixels drawn for each value, on one line and on several, are those of each line drawn
/// alone, left-aligned, where that value puts it. A text block breaks into lines at its line breaks and measures as wide
/// as its widest line; changing the alignment repaints in place, with no layout, shaping or allocation.
/// </summary>
public sealed class TextAlignmentTests
{
	private const float FontSize = 24f;
	private const int Width = 240;
	private const int Height = 120;

	private static readonly FontAtlas Font = FontAtlas.Default;
	private static readonly float LineHeight = Font.LineHeight * FontSize;

	private static readonly TextAlignment[] Every =
		[TextAlignment.Left, TextAlignment.Center, TextAlignment.Right, TextAlignment.Justify, TextAlignment.Start, TextAlignment.End];

	// A root `width` wide holding `content`, laid out.
	private static UIRoot Root(UIElement content, float width = Width)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(width, Height));
		return root;
	}

	private static SoftwareRasterizer Rasterize(UIRoot root)
	{
		var rasterizer = new SoftwareRasterizer(Width, Height);
		rasterizer.Render(root.Render());
		return rasterizer;
	}

	private static float WidthOf(string line) => Font.MeasureWidth(line) * FontSize;

	// Where a line of a text block `width` wide starts: as WinUI places it, its trailing whitespace hanging past.
	private static float Start(TextAlignment alignment, float width, string line) => alignment switch
	{
		TextAlignment.Center => (width - WidthOf(line.TrimEnd())) / 2f,
		TextAlignment.Right => width - WidthOf(line.TrimEnd()),
		_ => 0f,
	};

	// The lines of `text` drawn one by one, each a left-aligned text block placed where `alignment` puts that line.
	private static SoftwareRasterizer LineByLine(string text, TextAlignment alignment, float width)
	{
		var canvas = new Canvas();
		var lines = text.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			var line = new TextBlock { Text = lines[i], FontSize = FontSize };
			Canvas.SetLeft(line, Start(alignment, width, lines[i]));
			Canvas.SetTop(line, i * LineHeight);
			canvas.Children.Add(line);
		}
		return Rasterize(Root(canvas));
	}

	private static SoftwareRasterizer Aligned(string text, TextAlignment alignment, out TextBlock block)
	{
		block = new TextBlock { Text = text, FontSize = FontSize, TextAlignment = alignment };
		return Rasterize(Root(block));
	}

	// The largest difference of any channel of any pixel, in linear light.
	private static float Difference(SoftwareRasterizer a, SoftwareRasterizer b)
	{
		var worst = 0f;
		for (var y = 0; y < Height; y++)
		{
			for (var x = 0; x < Width; x++)
			{
				var d = Vector4.Abs(a.ReadLinear(x, y) - b.ReadLinear(x, y));
				worst = MathF.Max(worst, MathF.Max(MathF.Max(d.X, d.Y), MathF.Max(d.Z, d.W)));
			}
		}
		return worst;
	}

	// How much ink a frame holds.
	private static float Ink(SoftwareRasterizer r)
	{
		var sum = 0f;
		for (var y = 0; y < Height; y++)
		{
			for (var x = 0; x < Width; x++)
				sum += r.ReadLinear(x, y).W;
		}
		return sum;
	}

	[Test]
	[Arguments("Lap 1:23.456")]
	[Arguments("Wide first line\nab\n\nmid")]
	public void Each_value_draws_each_line_where_it_places_it(string text)
	{
		var left = Aligned(text, TextAlignment.Left, out var block);
		block.LayoutRect.Width.ShouldBe(Width); // stretched across the root, wider than its text
		Ink(left).ShouldBeGreaterThan(50f);

		foreach (var alignment in Every)
		{
			var aligned = Aligned(text, alignment, out _);
			Difference(aligned, LineByLine(text, alignment, Width)).ShouldBeLessThan(1f / 255f, $"{alignment}");
		}
		// Each value moved the text where it should (Start and End are Left and Right; Justify places no line it does not
		// wrap, and a text block does not wrap).
		Difference(Aligned(text, TextAlignment.Center, out _), left).ShouldBeGreaterThan(0.5f);
		Difference(Aligned(text, TextAlignment.Right, out _), left).ShouldBeGreaterThan(0.5f);
		Difference(Aligned(text, TextAlignment.Justify, out _), left).ShouldBe(0f);
		Difference(Aligned(text, TextAlignment.Start, out _), left).ShouldBe(0f);
		Difference(Aligned(text, TextAlignment.End, out _), Aligned(text, TextAlignment.Right, out _)).ShouldBe(0f);
	}

	[Test]
	public void Lines_are_aligned_within_the_width_the_text_block_is_arranged_at()
	{
		// Not stretched: arranged as wide as its widest line, so the shorter lines move within that width.
		const string text = "A much wider line\nnarrow\nmid one";
		foreach (var alignment in Every)
		{
			var block = new TextBlock { Text = text, FontSize = FontSize, TextAlignment = alignment, HorizontalAlignment = HorizontalAlignment.Left };
			var aligned = Rasterize(Root(block));
			block.LayoutRect.Width.ShouldBe(WidthOf("A much wider line"), 1e-3f);
			Difference(aligned, LineByLine(text, alignment, block.LayoutRect.Width)).ShouldBeLessThan(1f / 255f, $"{alignment}");
		}
	}

	[Test]
	public void Trailing_whitespace_hangs_past_where_a_line_is_placed()
	{
		foreach (var alignment in Every)
			Difference(Aligned("Gap 12  ", alignment, out _), Aligned("Gap 12", alignment, out _)).ShouldBe(0f, $"{alignment}");
		// Whitespace inside the line still counts.
		Difference(Aligned("Gap  12", TextAlignment.Right, out _), Aligned("Gap 12", TextAlignment.Right, out _)).ShouldBeGreaterThan(0.5f);
	}

	[Test]
	[Arguments("", 1)]
	[Arguments("One line", 1)]
	[Arguments("a\nb", 2)]
	[Arguments("a\r\nb", 2)]
	[Arguments("a\rb", 2)]
	[Arguments("a\u2028b\u2029c", 3)]
	[Arguments("ends with a break\n", 2)]
	[Arguments("\n\n", 3)]
	public void A_text_block_breaks_at_line_breaks_and_measures_its_widest_line(string text, int lines)
	{
		var block = new TextBlock { Text = text, FontSize = FontSize };
		block.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

		var widest = text.Split(['\r', '\n', '\u2028', '\u2029']).Max(WidthOf);
		block.DesiredSize.Width.ShouldBe(widest, 1e-3f);
		block.DesiredSize.Height.ShouldBe(lines * LineHeight, 1e-3f);
	}

	[Test]
	public void Each_line_starts_a_line_height_below_the_last_with_its_glyphs_in_order()
	{
		var root = Root(new TextBlock { Text = "ab\ncd", FontSize = FontSize });
		var draw = root.Render().Vertices.ToArray();
		var single = Root(new TextBlock { Text = "abcd", FontSize = FontSize }).Render().Vertices.ToArray();
		draw.Length.ShouldBe(single.Length);

		// a and b as on one line; c and d moved down a line and back to the start, by the width of "ab".
		var ab = WidthOf("ab");
		for (var i = 0; i < 8; i++)
			draw[i].Position.ShouldBe(single[i].Position);
		for (var i = 8; i < 16; i++)
		{
			draw[i].Position.X.ShouldBe(single[i].Position.X - ab, 1e-3f);
			draw[i].Position.Y.ShouldBe(single[i].Position.Y + LineHeight, 1e-3f);
		}
	}

	[Test]
	public void Realigning_repaints_in_place_with_no_layout_shaping_or_allocation()
	{
		var block = new TextBlock { Text = "Wide first line\nab", FontSize = FontSize };
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = block;
		host.Tick(0, 0f, new Vector2(Width, Height));
		host.Render();
		var content = block.Visual.Content.ShouldBeOfType<GlyphRunContent>();
		var shaped = content.Shape();

		block.TextAlignment = TextAlignment.Right;
		block.MeasureDirty.ShouldBeFalse();
		block.ArrangeDirty.ShouldBeFalse();
		block.Visual.Content.ShouldBeSameAs(content);
		content.TextAlignment.ShouldBe(TextAlignment.Right);
		block.Visual.Dump().ShouldContain(" align=right");

		const int measured = 64;
		for (var i = 0; i < 16; i++)
			Frame(i);
		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < measured; i++)
				Frame(i);
		});
		block.Visual.Content.ShouldBeSameAs(content);
		content.Shape().ShouldBeSameAs(shaped);

		void Frame(int i)
		{
			block.TextAlignment = (i % 3) switch { 0 => TextAlignment.Left, 1 => TextAlignment.Center, _ => TextAlignment.Right };
			host.Tick(1.0 / 60.0, 0f, new Vector2(Width, Height));
			host.Render();
		}
	}
}
