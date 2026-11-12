using System.Numerics;
using SUIsei.Composition;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// End to end over the build-time-generated <c>Sample/ShapesView.xaml</c>: each shape, its stroke set, its stretch,
/// its points or path markup and its render transform reach the runtime as written, and every shape draws.
/// </summary>
public sealed class ShapeViewTests
{
	private static ShapesView Laid()
	{
		var view = new ShapesView();
		view.InitializeComponent();
		var root = new UIRoot(new DrawListId(0)) { Content = view.Root };
		root.LayoutPass(new Size(200f, 120f));
		return view;
	}

	private static ShapeContent Drawn(UIElement shape) => shape.Visual.Content.ShouldBeOfType<ShapeContent>();

	[Test]
	public void Every_shape_draws_what_its_markup_says()
	{
		var view = Laid();

		var card = Drawn(view.Card);
		card.Fill.SolidColor().ShouldBe(HdrColor.FromRgb(0x33, 0x66, 0xCC));
		card.StrokeStyle.Thickness.ShouldBe(2f);
		card.Path!.Bounds.ShouldBeNear(new Rect(1, 1, 58, 38), 1e-4f);

		Drawn(view.Dot).Path!.Bounds.ShouldBeNear(new Rect(0, 0, 30, 30), 1e-3f);

		var rule = Drawn(view.Rule).StrokeStyle;
		rule.DashArray.ToArray().ShouldBe([4f, 2f]);
		rule.DashCap.ShouldBe(CompositionStrokeCap.Round);

		var trend = Drawn(view.Trend);
		trend.Path!.ToString().ShouldBe("M 0,20 L 20,5 L 40,15 L 60,0");
		(trend.StrokeStyle.LineJoin, trend.StrokeStyle.StartCap, trend.StrokeStyle.EndCap).ShouldBe((CompositionStrokeLineJoin.Round, CompositionStrokeCap.Round, CompositionStrokeCap.Triangle));

		Drawn(view.Badge).Path!.ToString().ShouldBe("F1 M 0,0 L 20,10 L 0,20 Z");

		var arrow = Drawn(view.Arrow);
		(arrow.StrokeStyle.MiterLimit, arrow.StrokeStyle.DashOffset).ShouldBe((4f, 0.5f));
		view.Arrow.RenderTransform.ShouldBeSameAs(view.ArrowSpin);
		// Uniform: the 30×30 arrow scaled into 50×50 with its pen of 1 inside.
		arrow.Bounds.ShouldBeNear(new Rect(0.5f, 0.5f, 49, 49), 1e-3f);
	}

	[Test]
	public void The_view_draws_through_the_vector_prim()
	{
		var view = new ShapesView();
		view.InitializeComponent();
		var root = new UIRoot(new DrawListId(0)) { Content = view.Root };
		root.LayoutPass(new Size(200f, 120f));

		var draw = root.Render();
		draw.Commands.Span.ToArray().ShouldAllBe(c => c.Tag == (uint)UIPrimKind.Vector);
		draw.Vertices.Length.ShouldBeGreaterThan(0);
	}
}
