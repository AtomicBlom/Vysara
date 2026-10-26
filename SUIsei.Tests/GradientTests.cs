using System.Numerics;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Rasterizer;

namespace SUIsei.Tests;

/// <summary>
/// Linear gradients drawn exactly, with no ramp texture: the compositor cuts what it paints where the colour stops being
/// linear and colours the pieces' corners, so the CPU rasterizer (the GPU's oracle) shows each stop's colour exactly,
/// interpolates linearly in linear light between ScRgb stops, keeps colours above paper white, stays within the
/// documented bound for sRGB interpolation, and draws no seam where the pieces meet.
/// </summary>
public sealed class GradientTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(255, 0, 0);
	private static readonly HdrColor Green = HdrColor.FromRgb(0, 255, 0);
	private static readonly HdrColor Blue = HdrColor.FromRgb(0, 0, 255);

	// A horizontal gradient across the area painted, through `stops` given as (offset, colour).
	private static LinearGradientBrush Horizontal(ColorInterpolationMode mode, params (float Offset, HdrColor Color)[] stops)
	{
		var brush = new LinearGradientBrush { EndPoint = new Point(1f, 0f), ColorInterpolationMode = mode };
		foreach (var (offset, color) in stops)
			brush.GradientStops.Add(new GradientStop { Offset = offset, Color = color });
		return brush;
	}

	// `element`, sized to fill a w×h surface, composited and rasterized over opaque black.
	private static SoftwareRasterizer Render(UIElement element, int w, int h, out DrawListData draw)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = element };
		root.LayoutPass(new Size(w, h));
		draw = root.Render();
		var rast = new SoftwareRasterizer(w, h);
		rast.Clear(new Vector4(0f, 0f, 0f, 1f));
		rast.Render(draw);
		return rast;
	}

	private static Border Box(Brush background, int w, int h) => new() { Width = w, Height = h, Background = background };

	private static void ShouldBeClose(Vector4 actual, Vector4 expected, float relative = 2e-3f)
	{
		var tolerance = relative * MathF.Max(1f, MathF.Max(MathF.Max(MathF.Abs(expected.X), MathF.Abs(expected.Y)), MathF.Abs(expected.Z)));
		Vector4.Abs(actual - expected).ShouldSatisfyAllConditions(
			d => d.X.ShouldBeLessThanOrEqualTo(tolerance),
			d => d.Y.ShouldBeLessThanOrEqualTo(tolerance),
			d => d.Z.ShouldBeLessThanOrEqualTo(tolerance),
			d => d.W.ShouldBeLessThanOrEqualTo(tolerance));
	}

	[Test]
	[Arguments(ColorInterpolationMode.ScRgbLinearInterpolation)]
	[Arguments(ColorInterpolationMode.SRgbLinearInterpolation)]
	public void Each_stop_s_colour_is_exact_in_the_draw_list_and_in_the_pixels(ColorInterpolationMode mode)
	{
		// 101 pixels wide, so the middle stop falls on pixel 50's centre and the end stops on the rect's edges.
		var brush = Horizontal(mode, (0f, Red), (0.5f, Green), (1f, Blue));
		var rast = Render(Box(brush, 101, 4), 101, 4, out var draw);

		var vertices = draw.Vertices.Span.ToArray();
		vertices.Where(v => v.Position.X == 50.5f).ShouldAllBe(v => v.Color == Green.ToPremultiplied());
		vertices.Where(v => v.Position.X == 0f).ShouldAllBe(v => v.Color == Red.ToPremultiplied());
		vertices.Where(v => v.Position.X == 101f).ShouldAllBe(v => v.Color == Blue.ToPremultiplied());
		vertices.Count(v => v.Position.X == 50.5f).ShouldBeGreaterThanOrEqualTo(2);

		var px = rast.ResolveToRgba8();
		var o = (2 * 101 + 50) * 4;
		(px[o], px[o + 1], px[o + 2], px[o + 3]).ShouldBe(((byte)0, (byte)255, (byte)0, (byte)255));
	}

	[Test]
	public void ScRgb_interpolation_is_linear_in_linear_light_between_stops()
	{
		var brush = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, Red), (0.3f, Green), (1f, Blue));
		var rast = Render(Box(brush, 100, 2), 100, 2, out _);
		var red = Red.ToPremultipliedVector();
		var green = Green.ToPremultipliedVector();
		var blue = Blue.ToPremultipliedVector();

		for (var x = 0; x < 100; x++)
		{
			var t = (x + 0.5f) / 100f;
			var expected = t < 0.3f ? Vector4.Lerp(red, green, t / 0.3f) : Vector4.Lerp(green, blue, (t - 0.3f) / 0.7f);
			ShouldBeClose(rast.ReadLinear(x, 1), expected);
		}
	}

	[Test]
	public void Stops_above_paper_white_survive_in_linear_light()
	{
		var bright = new HdrColor(8f, 6f, 4f, 1f);
		var brush = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, Red), (0.5f, bright), (1f, new HdrColor(0f, 0f, 3f, 1f)));
		var rast = Render(Box(brush, 101, 2), 101, 2, out var draw);

		draw.Vertices.Span.ToArray().Where(v => v.Position.X == 50.5f).ShouldAllBe(v => v.Color == bright.ToPremultiplied());
		var atStop = rast.ReadLinear(50, 1);
		atStop.X.ShouldBeGreaterThan(100f); // sRGB-encoded 8 is ~131 in linear light: far above paper white, unclamped
		ShouldBeClose(atStop, bright.ToPremultipliedVector());
		// Between the stops it is still linear in linear light.
		ShouldBeClose(rast.ReadLinear(75, 1), Vector4.Lerp(bright.ToPremultipliedVector(), new HdrColor(0f, 0f, 3f, 1f).ToPremultipliedVector(), (75.5f - 50.5f) / 50.5f));
	}

	[Test]
	public void An_sRGB_gradient_with_a_stop_above_paper_white_throws_when_drawn()
	{
		var brush = Horizontal(ColorInterpolationMode.SRgbLinearInterpolation, (0f, Red), (1f, new HdrColor(2f, 0f, 0f, 1f)));
		var root = new UIRoot(new DrawListId(0)) { Content = Box(brush, 10, 10) };
		root.LayoutPass(new Size(10f, 10f));

		Should.Throw<InvalidOperationException>(() => root.Render()).Message.ShouldContain("sRGB interpolation is not defined there");

		// The same stops are fine in linear light.
		brush.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
		Should.NotThrow(() => root.Render());
	}

	[Test]
	public void SRgb_interpolation_stays_within_its_documented_bound()
	{
		// A full black-to-white step, the worst case: the error grows with the square of the step between stops.
		var black = HdrColor.FromRgb(0, 0, 0);
		var white = HdrColor.FromRgb(255, 255, 255);
		var rast = Render(Box(Horizontal(ColorInterpolationMode.SRgbLinearInterpolation, (0f, black), (1f, white)), 256, 1), 256, 1, out _);
		var srgb = rast.ResolveToRgba8();
		var linear = Render(Box(Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, black), (1f, white)), 256, 1), 256, 1, out _).ResolveToRgba8();

		float worstLinear = 0f;
		int worstByte = 0, worstByteAboveMidGrey = 0;
		for (var x = 0; x < 256; x++)
		{
			// Interpolating the sRGB values is a straight ramp in them; the exact colour is that ramp in linear light.
			var s = (x + 0.5f) / 256f;
			worstLinear = MathF.Max(worstLinear, MathF.Abs(rast.ReadLinear(x, 0).X - new HdrColor(s, s, s, 1f).ToPremultipliedVector().X));
			var byteError = Math.Abs(srgb[x * 4] - (int)MathF.Round(s * 255f));
			worstByte = Math.Max(worstByte, byteError);
			if (s >= 0.5f)
				worstByteAboveMidGrey = Math.Max(worstByteAboveMidGrey, byteError);
		}
		worstLinear.ShouldBeLessThanOrEqualTo(0.006f);   // CompositionGradientBrush.SrgbSubBands' bound, in linear light
		worstByteAboveMidGrey.ShouldBeLessThanOrEqualTo(1); // under one 8-bit sRGB step where the curve is shallow
		worstByte.ShouldBeLessThanOrEqualTo(5);             // and a few near black, where the sRGB encode is steepest
															// The pieces are sampled exactly: the ramp passes through its middle exactly. Interpolating in linear light instead
															// is far brighter mid-way, so the two modes really differ.
		srgb[128 * 4].ShouldBe((byte)128);
		linear[128 * 4].ShouldBeGreaterThan((byte)180);
	}

	[Test]
	public void Translucent_pieces_meet_with_no_seam_and_a_quad_s_diagonal_is_drawn_once()
	{
		var glass = new HdrColor(0.2f, 0.6f, 1f, 0.5f);
		// Every stop the same colour, so the right picture is one flat colour; the pieces meet on pixel 50's centre.
		var brush = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, glass), (0.25f, glass), (0.5f, glass), (1f, glass));
		var banded = Render(Box(brush, 101, 101), 101, 101, out _).ResolveToRgba8();
		var flat = Render(Box(new SolidColorBrush(glass), 101, 101), 101, 101, out _).ResolveToRgba8();

		banded.ShouldBe(flat);
		// A square's diagonal runs through pixel centres; drawn twice, they would come out darker blue than the rest.
		for (var i = 0; i < flat.Length; i += 4)
			(flat[i], flat[i + 1], flat[i + 2]).ShouldBe((flat[0], flat[1], flat[2]));
	}

	[Test]
	public void A_rounded_rect_and_its_border_keep_their_edges_when_cut_into_bands()
	{
		var color = HdrColor.FromRgb(240, 200, 60);
		var edge = HdrColor.FromRgb(30, 90, 200);
		Border Shape(Brush fill, Brush stroke) => new()
		{
			Width = 60,
			Height = 40,
			CornerRadius = new CornerRadius(12f),
			Background = fill,
			BorderBrush = stroke,
			BorderThickness = new Thickness(3f),
		};

		var banded = Render(Shape(
			Horizontal(ColorInterpolationMode.SRgbLinearInterpolation, (0f, color), (0.33f, color), (0.71f, color), (1f, color)),
			Horizontal(ColorInterpolationMode.SRgbLinearInterpolation, (0f, edge), (0.5f, edge), (1f, edge))), 60, 40, out var draw).ResolveToRgba8();
		var solid = Render(Shape(new SolidColorBrush(color), new SolidColorBrush(edge)), 60, 40, out var solidDraw).ResolveToRgba8();

		draw.Vertices.Length.ShouldBeGreaterThan(solidDraw.Vertices.Length); // really cut into pieces
		MaxDifference(banded, solid).ShouldBeLessThanOrEqualTo(1);           // with the same corners and ring
	}

	[Test]
	public void Gradient_text_is_cut_into_pieces_that_keep_their_glyphs()
	{
		var ink = HdrColor.FromRgb(250, 250, 250);
		TextBlock Text(Brush brush) => new() { Text = "Gradient", FontSize = 32f, Foreground = brush };

		var banded = Render(Text(Horizontal(ColorInterpolationMode.SRgbLinearInterpolation, (0f, ink), (0.21f, ink), (0.52f, ink), (1f, ink))), 200, 48, out var draw).ResolveToRgba8();
		var solid = Render(Text(new SolidColorBrush(ink)), 200, 48, out var solidDraw).ResolveToRgba8();

		draw.Vertices.Length.ShouldBeGreaterThan(solidDraw.Vertices.Length);
		MaxDifference(banded, solid).ShouldBeLessThanOrEqualTo(1);
		solid.Count(b => b > 128).ShouldBeGreaterThan(500); // there is text
	}

	[Test]
	public void Text_takes_a_real_gradient_across_its_layout_box()
	{
		var text = new TextBlock { Text = "IIIIIIIIII", FontSize = 40f, Foreground = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, Red), (1f, Blue)) };
		Render(text, 200, 60, out var draw);

		// Glyph corners take the gradient's colour at their place in the text block's box.
		var width = text.LayoutRect.Width;
		foreach (var v in draw.Vertices.Span)
			ShouldBeClose(v.Color.ToVector4(), Vector4.Lerp(Red.ToPremultipliedVector(), Blue.ToPremultipliedVector(), Math.Clamp(v.Position.X / width, 0f, 1f)), 4e-3f);
	}

	[Test]
	[Arguments(GradientSpreadMethod.Pad, 1f)]
	[Arguments(GradientSpreadMethod.Repeat, 0.275f)]
	[Arguments(GradientSpreadMethod.Reflect, 0.725f)]
	public void Spread_methods_pad_repeat_and_reflect_past_the_end(GradientSpreadMethod spread, float u)
	{
		// The gradient spans the first 20 pixels; pixel 25's centre lies at t = 1.275.
		var brush = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, Red), (1f, Blue));
		brush.EndPoint = new Point(0.2f, 0f);
		brush.SpreadMethod = spread;
		var rast = Render(Box(brush, 100, 2), 100, 2, out _);

		ShouldBeClose(rast.ReadLinear(25, 1), Vector4.Lerp(Red.ToPremultipliedVector(), Blue.ToPremultipliedVector(), u));
	}

	[Test]
	public void A_diagonal_gradient_is_constant_across_its_line_and_absolute_points_are_in_DIPs()
	{
		var diagonal = new LinearGradientBrush
		{
			ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
			GradientStops = { new GradientStop { Color = Red }, new GradientStop { Color = Blue, Offset = 1f } },
		};
		var rast = Render(Box(diagonal, 100, 100), 100, 100, out _);
		ShouldBeClose(rast.ReadLinear(10, 80), rast.ReadLinear(80, 10), 1e-3f);
		ShouldBeClose(rast.ReadLinear(45, 45), rast.ReadLinear(10, 80), 1e-3f);
		ShouldBeClose(rast.ReadLinear(45, 45), Vector4.Lerp(Red.ToPremultipliedVector(), Blue.ToPremultipliedVector(), 91f / 200f));

		var absolute = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, Red), (1f, Blue));
		absolute.MappingMode = BrushMappingMode.Absolute;
		absolute.StartPoint = new Point(10f, 0f);
		absolute.EndPoint = new Point(30f, 0f);
		var line = Render(Box(absolute, 100, 2), 100, 2, out _);
		ShouldBeClose(line.ReadLinear(20, 1), Vector4.Lerp(Red.ToPremultipliedVector(), Blue.ToPremultipliedVector(), 10.5f / 20f));
		ShouldBeClose(line.ReadLinear(5, 1), Red.ToPremultipliedVector());
		ShouldBeClose(line.ReadLinear(60, 1), Blue.ToPremultipliedVector());
	}

	[Test]
	public void A_gradient_whose_start_and_end_coincide_paints_its_last_stop()
	{
		var brush = Horizontal(ColorInterpolationMode.ScRgbLinearInterpolation, (0f, Red), (1f, Blue));
		brush.EndPoint = brush.StartPoint;
		var rast = Render(Box(brush, 10, 10), 10, 10, out _);

		ShouldBeClose(rast.ReadLinear(5, 5), Blue.ToPremultipliedVector());
	}

	[Test]
	public void A_gradient_with_no_stops_paints_nothing()
	{
		var rast = Render(Box(new LinearGradientBrush(), 10, 10), 10, 10, out var draw);

		draw.Vertices.Length.ShouldBe(0);
		rast.ReadLinear(5, 5).ShouldBe(new Vector4(0f, 0f, 0f, 1f));
	}

	private static int MaxDifference(byte[] a, byte[] b)
	{
		a.Length.ShouldBe(b.Length);
		var worst = 0;
		for (var i = 0; i < a.Length; i++)
			worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
		return worst;
	}
}
