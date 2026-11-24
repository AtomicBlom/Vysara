using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="UIRoot.UseLayoutRounding"/>: every element's edges on whole pixels of the surface, at any layout scale, so a
/// line moved by fractions of a pixel steps a whole pixel at a time; neighbours still meet; a new scale rounds again; and
/// with it off, layout is what it always was.
/// </summary>
public sealed class LayoutRoundingTests
{
	private const float Scale = 1.5f;

	// Where an element is on the surface, in pixels: its own and each parent's offset, scaled
	private static (float Left, float Right) Pixels(UIElement element, float scale)
	{
		var x = 0f;
		for (var e = element; e is not null; e = e.Parent)
			x += e.LayoutRect.X;
		return (x * scale, (x + element.LayoutRect.Width) * scale);
	}

	private static void ShouldBeWhole(float pixels) => MathF.Abs(pixels - MathF.Round(pixels)).ShouldBeLessThan(1e-3f);

	private static (UIRoot Root, Border Line, Canvas Canvas) Lined(bool rounding, float left)
	{
		var line = new Border { Width = 2f, Height = 30f };
		var canvas = new Canvas { Width = 300f, Height = 30f, Margin = new Thickness(7.3f, 0f, 0f, 0f) };
		Canvas.SetLeft(line, left);
		canvas.Children.Add(line);
		var panel = new StackPanel { Margin = new Thickness(3.7f, 0f, 0f, 0f) };
		panel.Children.Add(canvas);
		var root = new UIRoot(new DrawListId(0)) { LayoutScale = Scale, UseLayoutRounding = rounding, Content = panel };
		root.LayoutPass(new Size(600f, 100f));
		return (root, line, canvas);
	}

	[Test]
	public void A_line_at_any_position_lands_on_whole_pixels_and_keeps_its_width()
	{
		foreach (var left in new[] { 0f, 10.1f, 10.4f, 10.77f, 133.333f })
		{
			var (_, line, _) = Lined(rounding: true, left);
			var (from, to) = Pixels(line, Scale);
			ShouldBeWhole(from);
			ShouldBeWhole(to);
			(to - from).ShouldBe(3f, 1e-3f); // 2 DIPs at 1.5 is 3 pixels wherever it is
		}
	}

	[Test]
	public void Without_rounding_layout_is_as_it_was()
	{
		var (_, line, _) = Lined(rounding: false, 10.4f);
		line.LayoutRect.X.ShouldBe(10.4f, 1e-4f);
		line.LayoutRect.Width.ShouldBe(2f);
	}

	[Test]
	public void Neighbours_still_meet()
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.4f) };
		for (var i = 0; i < 5; i++)
			row.Children.Add(new Border { Width = 10.3f, Height = 4f });
		var root = new UIRoot(new DrawListId(0)) { LayoutScale = Scale, UseLayoutRounding = true, Content = row };
		root.LayoutPass(new Size(400f, 100f));

		for (var i = 0; i < row.Children.Count; i++)
		{
			var (from, to) = Pixels(row.Children[i], Scale);
			ShouldBeWhole(from);
			ShouldBeWhole(to);
			if (i > 0)
				from.ShouldBe(Pixels(row.Children[i - 1], Scale).Right, 1e-3f);
		}
	}

	[Test]
	public void A_new_scale_rounds_to_its_own_pixels()
	{
		var (root, line, _) = Lined(rounding: true, 10.4f);
		root.LayoutScale = 1.25f;
		root.LayoutPass(new Size(600f, 100f));
		var (from, to) = Pixels(line, 1.25f);
		ShouldBeWhole(from);
		ShouldBeWhole(to);
	}
}
