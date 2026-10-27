using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Extensions;
using SUIsei.Media;
using SUIsei.Rasterizer;
using SUIsei.Shapes;
using SUIsei.Tests.Sample;
using Path = SUIsei.Shapes.Path;

namespace SUIsei.Tests;

/// <summary>
/// The <c>suisei:</c> stroke extensions on a shape: <see cref="StrokeTrim"/> trims the drawn stroke in place, with no
/// layout and no tessellation, animatable from markup through <c>(suisei:StrokeTrim.End)</c> and allocation-free;
/// <see cref="StrokeBrushMapping"/> runs a stroke gradient along the stroke's untrimmed length, exactly at its stops; and
/// the generator reports either set or animated on something that is not a shape at its XAML line.
/// </summary>
public sealed class StrokeExtensionsTests
{
	private static readonly HdrColor Red = HdrColor.FromRgb(255, 0, 0);
	private static readonly HdrColor Green = HdrColor.FromRgb(0, 255, 0);
	private static readonly HdrColor Blue = HdrColor.FromRgb(0, 0, 255);

	private static UIRoot Laid(UIElement content)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(100f, 100f));
		return root;
	}

	private static Canvas CanvasOf(UIElement child)
	{
		var canvas = new Canvas();
		canvas.Children.Add(child);
		return canvas;
	}

	private static SoftwareRasterizer Raster(DrawListData draw)
	{
		var raster = new SoftwareRasterizer(100, 100);
		raster.Clear(new Vector4(0f, 0f, 0f, 1f));
		raster.Render(draw);
		return raster;
	}

	private static void ShouldBeClose(Vector4 actual, Vector4 expected, float tolerance = 2e-3f) =>
		Vector4.Distance(actual, expected).ShouldBeLessThanOrEqualTo(tolerance, $"{actual} should be within {tolerance} of {expected}");

	private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

	// A corner of two 40-long legs, 10,50 → 50,50 → 50,90: 80 long, its corner halfway along.
	private static Polyline Corner(Brush stroke) =>
		new() { Points = { new Point(10, 50), new Point(50, 50), new Point(50, 90) }, Stroke = stroke, StrokeThickness = 6f };

	private static LinearGradientBrush ThreeStops() => new()
	{
		ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
		GradientStops = { new GradientStop { Color = Red }, new GradientStop { Color = Green, Offset = 0.5f }, new GradientStop { Color = Blue, Offset = 1f } },
	};

	private static Vector4 Along(float t) => t <= 0.5f
		? Vector4.Lerp(Red.ToPremultipliedVector(), Green.ToPremultipliedVector(), t / 0.5f)
		: Vector4.Lerp(Green.ToPremultipliedVector(), Blue.ToPremultipliedVector(), (t - 0.5f) / 0.5f);

	[Test]
	public void A_trim_trims_the_drawn_stroke_in_place_with_no_layout_or_tessellation()
	{
		var line = Corner(new SolidColorBrush(Red));
		var root = Laid(CanvasOf(line));
		root.Render();
		var shape = line.Visual.Content.ShouldBeOfType<ShapeContent>();
		var stroke = shape.TessellateStroke(TessellationScale.Identity)!;
		var version = stroke.Version;
		(StrokeTrim.GetStart(line), StrokeTrim.GetEnd(line), StrokeTrim.GetOffset(line)).ShouldBe((0f, 1f, 0f));

		StrokeTrim.SetEnd(line, 0.5f);

		shape.Trim.ShouldBe(new PathTrim(0f, 0.5f, 0f));
		(line.MeasureDirty, line.ArrangeDirty).ShouldBe((false, false));
		line.Visual.IsDirty.ShouldBeTrue();
		var raster = Raster(root.Render());
		stroke.Version.ShouldBe(version);
		ShouldBeClose(raster.ReadLinear(30, 50), Red.ToPremultipliedVector());
		ShouldBeClose(raster.ReadLinear(50, 70), Black); // past the trim, on the second leg

		StrokeTrim.SetOffset(line, 0.5f);
		raster = Raster(root.Render());
		ShouldBeClose(raster.ReadLinear(30, 50), Black);
		ShouldBeClose(raster.ReadLinear(50, 70), Red.ToPremultipliedVector());
		line.Visual.Dump().ShouldEndWith(" trim=0..0.5+0.5\n");
	}

	[Test]
	public void Along_the_length_a_stroke_gradient_lands_its_stops_on_their_offsets()
	{
		var along = Corner(ThreeStops());
		StrokeBrushMapping.SetMode(along, StrokeBrushMappingMode.AlongLength);
		var draw = Laid(CanvasOf(along)).Render();

		// The corner, halfway along, takes the middle stop exactly: every vertex there, join and leg ends alike.
		var atCorner = draw.Vertices.Span.ToArray().Where(v => Vector2.Distance(v.Position, new Vector2(50, 50)) < 1e-4f).ToArray();
		atCorner.ShouldNotBeEmpty();
		atCorner.ShouldAllBe(v => v.Color == Green.ToPremultiplied());
		var raster = Raster(draw);
		ShouldBeClose(raster.ReadLinear(30, 50), Along(20.5f / 80f)); // pixel centre 30.5 is 20.5 along the first leg
		ShouldBeClose(raster.ReadLinear(50, 70), Along(60.5f / 80f)); // 70.5 is 20.5 down the second
		along.Visual.Dump().ShouldContain(" along-length");

		// The brush's own mapping measures across the shape's bounds instead: here left to right over its 40 wide.
		var across = ThreeStops();
		across.EndPoint = new Point(1, 0);
		var spatial = Corner(across);
		ShouldBeClose(Raster(Laid(CanvasOf(spatial)).Render()).ReadLinear(30, 50), Along(20.5f / 40f));
	}

	[Test]
	public void Along_the_length_runs_over_the_untrimmed_stroke()
	{
		var trimmed = Corner(ThreeStops());
		StrokeBrushMapping.SetMode(trimmed, StrokeBrushMappingMode.AlongLength);
		StrokeTrim.SetStart(trimmed, 0.25f);
		var raster = Raster(Laid(CanvasOf(trimmed)).Render());

		ShouldBeClose(raster.ReadLinear(50, 70), Along(60.5f / 80f));
		ShouldBeClose(raster.ReadLinear(20, 50), Black); // before the trim's start
	}

	[Test]
	public void A_storyboard_on_the_trim_draws_a_shape_on_from_markup()
	{
		var view = new StrokeExtensionsView();
		view.InitializeComponent();
		var host = new UIHost();
		var root = host.CreateRoot(UILayer.Default).Root;
		root.Content = view.Root;
		host.Tick(0, 0f, new Vector2(100f, 120f));
		StrokeBrushMapping.GetMode(view.Trail).ShouldBe(StrokeBrushMappingMode.AlongLength);
		StrokeTrim.GetEnd(view.Trail).ShouldBe(0f);

		VisualStateManager.GoToState(view.Host, "Drawn", useTransitions: true).ShouldBeTrue();
		host.Tick(0.5, 0f, new Vector2(100f, 120f));

		StrokeTrim.GetEnd(view.Trail).ShouldBe(0.5f, 1e-4f);
		var raster = Raster(root.Render());
		ShouldBeClose(raster.ReadLinear(30, 50), Along(20.5f / 80f));
		ShouldBeClose(raster.ReadLinear(50, 70), Black);

		host.Tick(1.0, 0f, new Vector2(100f, 120f));
		ShouldBeClose(Raster(root.Render()).ReadLinear(50, 70), Along(60.5f / 80f));
	}

	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Animating_a_shapes_trim_colour_and_transform_allocates_nothing_once_warm()
	{
		var paint = new SolidColorBrush(Red);
		var gradient = ThreeStops();
		var drawOn = new Path
		{
			Data = Geometry.Parse("M 10,10 C 10,90 90,90 90,10"),
			Stroke = paint,
			StrokeThickness = 3f,
			StrokeDashArray = [4, 2],
			RenderTransform = new CompositeTransform(),
		};
		var trail = new Polyline { Points = { new Point(0, 0), new Point(40, 20), new Point(80, 0) }, Stroke = gradient, StrokeThickness = 2f };
		StrokeBrushMapping.SetMode(trail, StrokeBrushMappingMode.AlongLength);
		var canvas = new Canvas();
		canvas.Children.Add(drawOn);
		canvas.Children.Add(trail);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = canvas;

		// Storyboards, as XAML writes them: the trim's end and offset, the stroke's colour and the shape's rotation.
		host.Clock.Begin(new Storyboard
		{
			RepeatBehavior = RepeatBehavior.Forever,
			Children =
			{
				new DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromSeconds(0.5), Target = drawOn, TargetProperty = StrokeTrim.EndProperty },
				new DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromSeconds(0.5), Target = trail, TargetProperty = StrokeTrim.OffsetProperty },
				new ColorAnimation { To = Blue, Duration = TimeSpan.FromSeconds(0.5), Target = drawOn, TargetProperty = new PropertyPath(Shape.StrokeProperty, SolidColorBrush.ColorProperty) },
				new DoubleAnimation { To = 90.0, Duration = TimeSpan.FromSeconds(0.5), Target = drawOn, TargetProperty = new PropertyPath(UIElement.RenderTransformProperty, CompositeTransform.RotationProperty) },
			},
		});
		var frame = 0;
		void Frame() => host.Tick(++frame * 0.016, 0.016f, new Vector2(200f, 200f));

		for (var i = 0; i < 40; i++)
			Frame();
		var stroke = ((ShapeContent)drawOn.Visual.Content!).TessellateStroke(TessellationScale.For(drawOn.Visual.Transform))!;
		var version = stroke.Version;
		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 30; i++)
				Frame();
		});
		StrokeTrim.GetEnd(drawOn).ShouldBeInRange(0f, 1f);
		stroke.Version.ShouldBe(version); // trimming re-tessellated nothing
	}
}
