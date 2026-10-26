using SUIsei.Media;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// End to end over the build-time-generated <c>Sample/GeometryView.xaml</c>: the clip, the path markup and the element
/// form of the same geometry reach the runtime as the objects they name.
/// </summary>
public sealed class GeometryViewTests
{
	private static GeometryView Build()
	{
		var view = new GeometryView();
		view.InitializeComponent();
		return view;
	}

	[Test]
	public void The_clip_property_element_sets_a_rectangle_geometry()
	{
		Build().Clipped.Clip.ShouldNotBeNull().Rect.ShouldBe(new Rect(5, 5, 20, 10));
	}

	[Test]
	public void Path_markup_and_the_element_form_build_the_same_geometry()
	{
		var view = Build();
		var fromMarkup = view.FromMarkup.Data.ShouldBeOfType<PathGeometry>();
		var fromElements = view.FromElements.Data.ShouldBeOfType<PathGeometry>();

		fromMarkup.FillRule.ShouldBe(FillRule.Nonzero);
		fromMarkup.Figures.Count.ShouldBe(2);
		fromMarkup.CompositionPath.ToString().ShouldBe(fromElements.CompositionPath.ToString());
		fromMarkup.ToMarkup().ShouldBe(fromElements.ToMarkup());
	}

	[Test]
	public void Point_and_number_lists_convert()
	{
		var host = Build().FromMarkup;
		host.Points.ShouldBe([new Point(0, 0), new Point(1, 1), new Point(2, 0)]);
		host.Dashes.ShouldBe([2d, 1d]);
	}
}
