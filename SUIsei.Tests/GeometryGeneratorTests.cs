using Microsoft.CodeAnalysis;

namespace SUIsei.Tests;

/// <summary>
/// Geometry in XAML: path markup checked at compile time by the runtime's own parser, with a mistake reported as
/// SUISEI024 at its XAML line; the point, number-list, rect and size converters; and geometry written as elements, as
/// <see cref="UIElement.Clip"/>'s property element and as figures and segments.
/// </summary>
public sealed class GeometryGeneratorTests
{
	// A test element with a property of each type the geometry converters fill.
	private const string CodeBehind = """
		namespace Demo
		{
			public partial class V { }
			public sealed class Plot : SUIsei.Controls.ContentControl
			{
				public SUIsei.Media.Geometry? Data { get; set; }
				public SUIsei.Media.PointCollection Points { get; set; } = new();
				public SUIsei.Media.DoubleCollection Dashes { get; set; } = new();
				public SUIsei.Rect Area { get; set; }
				public SUIsei.Size Extent { get; set; }
			}
		}
		""";

	private static string View(string body) => $$"""
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            xmlns:local="using:Demo"
		            x:Class="Demo.V">
		  {{body}}
		</StackPanel>
		""";

	// The XAML line (zero-based) the body's first line lands on in View.
	private const int BodyLine = 4;

	private static string Generates(string body)
	{
		var xaml = View(body);
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		result.Diagnostics.ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
		return result.AllGeneratedText();
	}

	private static Diagnostic Fails(string body)
	{
		var diagnostic = XamlGeneratorHarness.Run(View(body), CodeBehind).Diagnostics.ShouldHaveSingleItem();
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		diagnostic.Location.GetLineSpan().StartLinePosition.Line.ShouldBe(BodyLine);
		return diagnostic;
	}

	[Test]
	public void Path_markup_is_checked_then_parsed_by_the_runtime()
	{
		Generates("""<local:Plot Data="F1 M 0,0 L 10,0 10,10 Z" />""")
			.ShouldContain("""global::SUIsei.Media.Geometry.Parse("F1 M 0,0 L 10,0 10,10 Z")""");
	}

	[Test]
	[Arguments("M 0,0 L 10", "at character 7: 'L' needs 2 numbers")]
	[Arguments("L 1,1", "at character 1: 'L' comes before any move")]
	[Arguments("M 0,0 A 5,5 0 2 0 1,1", "large-arc flag is 0 or 1")]
	[Arguments("", "the path has no figures")]
	public void Bad_path_markup_is_SUISEI024_saying_where(string markup, string message)
	{
		var diagnostic = Fails($"""<local:Plot Data="{markup}" />""");
		diagnostic.Id.ShouldBe("SUISEI024");
		diagnostic.GetMessage().ShouldContain("for property 'Data'");
		diagnostic.GetMessage().ShouldContain(message);
	}

	[Test]
	public void Path_markup_in_a_property_element_is_checked_too()
	{
		var diagnostic = Fails("""<local:Plot><local:Plot.Data>M 0,0 Q 1</local:Plot.Data></local:Plot>""");
		diagnostic.Id.ShouldBe("SUISEI024");
		diagnostic.GetMessage().ShouldContain("'Q' needs 4 numbers");
	}

	[Test]
	[Arguments("""<local:Plot Points="0,0 10,5 20,0" />""", "new global::SUIsei.Media.PointCollection { new global::SUIsei.Point(0f, 0f), new global::SUIsei.Point(10f, 5f), new global::SUIsei.Point(20f, 0f) }")]
	[Arguments("""<local:Plot Points="" />""", "new global::SUIsei.Media.PointCollection()")]
	[Arguments("""<local:Plot Dashes="2 1.5,0.25" />""", "new global::SUIsei.Media.DoubleCollection { 2d, 1.5d, 0.25d }")]
	[Arguments("""<local:Plot Area="1,2,30,40" />""", "new global::SUIsei.Rect(1f, 2f, 30f, 40f)")]
	[Arguments("""<local:Plot Extent="3,4" />""", "new global::SUIsei.Size(3f, 4f)")]
	public void The_geometry_value_converters_emit_typed_values(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("""<local:Plot Points="0,0 10" />""", "pairs of numbers")]
	[Arguments("""<local:Plot Dashes="2 dash" />""", "numbers separated by commas or spaces")]
	[Arguments("""<local:Plot Area="1,2,-3,4" />""", "a width and height of 0 or more")]
	[Arguments("""<local:Plot Area="1,2,3" />""", "four numbers")]
	[Arguments("""<local:Plot Extent="3" />""", "two numbers of 0 or more")]
	public void A_bad_geometry_value_is_SUISEI012_naming_the_syntax(string body, string syntax)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("SUISEI012");
		diagnostic.GetMessage().ShouldContain(syntax);
	}

	[Test]
	public void A_clip_is_a_RectangleGeometry_property_element()
	{
		Generates("""<Border><Border.Clip><RectangleGeometry Rect="0,0,50,20" /></Border.Clip></Border>""")
			.ShouldContain(".Clip = ");
	}

	[Test]
	public void Geometry_can_be_written_as_figures_segments_and_groups()
	{
		var code = Generates("""
			<local:Plot>
			  <local:Plot.Data>
			    <GeometryGroup FillRule="Nonzero">
			      <PathGeometry FillRule="Nonzero">
			        <PathFigure StartPoint="0,0" IsClosed="True" IsFilled="False">
			          <LineSegment Point="10,0" />
			          <PolyLineSegment Points="10,10 0,10" />
			          <BezierSegment Point1="1,1" Point2="2,2" Point3="3,3" />
			          <PolyBezierSegment Points="1,1 2,2 3,3" />
			          <QuadraticBezierSegment Point1="1,1" Point2="2,2" />
			          <PolyQuadraticBezierSegment Points="1,1 2,2" />
			          <ArcSegment Point="5,5" Size="3,3" RotationAngle="15" IsLargeArc="True" SweepDirection="Clockwise" />
			        </PathFigure>
			      </PathGeometry>
			      <RectangleGeometry Rect="0,0,4,4" />
			      <EllipseGeometry Center="2,2" RadiusX="2" RadiusY="1" />
			      <LineGeometry StartPoint="0,0" EndPoint="4,4" />
			    </GeometryGroup>
			  </local:Plot.Data>
			</local:Plot>
			""");

		code.ShouldContain(".Segments.Add(");
		code.ShouldContain(".Figures.Add(");
		code.ShouldContain(".Children.Add(");
	}
}
