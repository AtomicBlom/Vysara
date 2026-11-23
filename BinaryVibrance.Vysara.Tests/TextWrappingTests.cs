using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;
using BinaryVibrance.Vysara.Rasterizer;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="TextBlock.TextWrapping"/> breaks a text block's lines at spaces to keep inside the width it is measured at,
/// and <see cref="TextBlock.TextTrimming"/> cuts a line still too wide short with an ellipsis, as WinUI's do: the pixels
/// drawn are those of each expected line drawn alone, and the text block measures as tall as its lines.
/// </summary>
public sealed class TextWrappingTests
{
	private const float FontSize = 24f;
	private const int Width = 240;
	private const int Height = 200;

	private static readonly FontAtlas Font = FontAtlas.Default;
	private static readonly float LineHeight = Font.LineHeight * FontSize;

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

	// The lines given drawn one by one, each a text block of its own, one under another
	private static SoftwareRasterizer LineByLine(params string[] lines)
	{
		var canvas = new Canvas();
		for (var i = 0; i < lines.Length; i++)
		{
			var line = new TextBlock { Text = lines[i], FontSize = FontSize };
			Canvas.SetTop(line, i * LineHeight);
			canvas.Children.Add(line);
		}
		return Rasterize(Root(canvas));
	}

	// Words taken greedily onto lines no wider than the width, as WinUI wraps
	private static string[] Greedy(string text, float width)
	{
		var lines = new List<string>();
		var line = "";
		foreach (var word in text.Split(' '))
		{
			var tried = line.Length == 0 ? word : line + " " + word;
			if (line.Length > 0 && WidthOf(tried) > width)
			{
				lines.Add(line + " ");
				line = word;
			}
			else
				line = tried;
		}
		lines.Add(line);
		return [.. lines];
	}

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

	// The rightmost column with any ink
	private static int RightmostInk(SoftwareRasterizer r)
	{
		for (var x = Width - 1; x >= 0; x--)
			for (var y = 0; y < Height; y++)
				if (r.ReadLinear(x, y).W > 0.01f)
					return x;
		return -1;
	}

	private const string Sentence = "Playing online: others' cars on your track as ghosts, standings, and race events";

	[Test]
	public void Wrapped_lines_are_the_words_that_fit_each_line()
	{
		var block = new TextBlock { Text = Sentence, FontSize = FontSize, TextWrapping = TextWrapping.Wrap };
		var drawn = Rasterize(Root(block));
		var lines = Greedy(Sentence, Width);
		lines.Length.ShouldBeGreaterThan(2);
		block.DesiredSize.Height.ShouldBe(lines.Length * LineHeight, 1e-3f);
		foreach (var line in lines)
			WidthOf(line.TrimEnd()).ShouldBeLessThanOrEqualTo(Width);
		Difference(drawn, LineByLine(lines)).ShouldBeLessThan(1f / 255f);
	}

	[Test]
	public void Without_wrapping_the_text_runs_on_past_the_width()
	{
		var block = new TextBlock { Text = Sentence, FontSize = FontSize, HorizontalAlignment = HorizontalAlignment.Left };
		Root(block);
		block.DesiredSize.Height.ShouldBe(LineHeight, 1e-3f);
		WidthOf(Sentence).ShouldBeGreaterThan(Width);
	}

	[Test]
	public void A_word_wider_than_the_width_keeps_a_line_of_its_own()
	{
		const string text = "a Supercalifragilisticexpialidocious b";
		var block = new TextBlock { Text = text, FontSize = FontSize, TextWrapping = TextWrapping.Wrap };
		var drawn = Rasterize(Root(block));
		Difference(drawn, LineByLine("a ", "Supercalifragilisticexpialidocious ", "b")).ShouldBeLessThan(1f / 255f);
	}

	[Test]
	public void Line_breaks_still_break_and_each_line_wraps_on_its_own()
	{
		const string text = "Short\nA line here that is long enough to wrap twice over";
		var block = new TextBlock { Text = text, FontSize = FontSize, TextWrapping = TextWrapping.Wrap };
		var drawn = Rasterize(Root(block));
		string[] lines = ["Short", .. Greedy("A line here that is long enough to wrap twice over", Width)];
		Difference(drawn, LineByLine(lines)).ShouldBeLessThan(1f / 255f);
	}

	[Test]
	public void A_narrower_width_wraps_again()
	{
		var block = new TextBlock { Text = Sentence, FontSize = FontSize, TextWrapping = TextWrapping.Wrap };
		var root = Root(block);
		float wide = block.DesiredSize.Height;
		root.LayoutPass(new Size(Width / 2f, 10 * Height));   // tall enough not to cap the lines
		block.DesiredSize.Height.ShouldBe(Greedy(Sentence, Width / 2f).Length * LineHeight, 1e-3f);
		block.DesiredSize.Height.ShouldBeGreaterThan(wide);
	}

	[Test]
	public void A_trimmed_line_ends_in_an_ellipsis_inside_the_width()
	{
		var block = new TextBlock { Text = Sentence, FontSize = FontSize, TextTrimming = TextTrimming.CharacterEllipsis };
		var drawn = Rasterize(Root(block));
		block.DesiredSize.Height.ShouldBe(LineHeight, 1e-3f);
		RightmostInk(drawn).ShouldBeLessThan(Width);
		// The most of the line that fits with the ellipsis after it
		var ellipsis = Font.TryGetGlyph('…', out _) ? "…" : "...";
		var kept = Sentence.Length;
		while (WidthOf(Sentence[..kept].TrimEnd() + ellipsis) > Width)
			kept--;
		Difference(drawn, LineByLine(Sentence[..kept].TrimEnd() + ellipsis)).ShouldBeLessThan(1f / 255f);
	}

	[Test]
	public void Text_that_fits_is_not_trimmed()
	{
		var trimmed = Rasterize(Root(new TextBlock { Text = "Lap 1:23.456", FontSize = FontSize, TextTrimming = TextTrimming.CharacterEllipsis }));
		Difference(trimmed, LineByLine("Lap 1:23.456")).ShouldBe(0f);
	}

	[Test]
	public void Wrapping_and_trimming_cut_only_a_word_too_wide_for_any_line()
	{
		const string text = "a Supercalifragilisticexpialidociously b";
		var block = new TextBlock { Text = text, FontSize = FontSize, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
		var drawn = Rasterize(Root(block));
		RightmostInk(drawn).ShouldBeLessThan(Width);
		block.DesiredSize.Height.ShouldBe(3 * LineHeight, 1e-3f);
	}
}
