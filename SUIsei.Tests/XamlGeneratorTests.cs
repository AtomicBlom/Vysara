using SUIsei.Controls;
using SUIsei.Tests.Generated;

namespace SUIsei.Tests;

/// <summary>
/// End-to-end proof: <c>Sample/SampleView.xaml</c> is compiled by
/// SUIsei.SourceGen into a partial <see cref="SampleView"/> with typed <c>x:Name</c> fields and an
/// <c>InitializeComponent()</c> that builds the tree with generated <c>new</c> (no reflection, no backend).
/// These tests construct that tree and assert the generated names / literal+enum properties / container wiring,
/// then run it through a <see cref="UIRoot"/> layout + compose pass to prove it is a real, composable tree.
/// </summary>
public sealed class XamlGeneratorTests
{
	private static SampleView BuiltView()
	{
		var view = new SampleView();
		view.InitializeComponent();
		return view;
	}

	[Test]
	public void Generated_root_is_the_typed_xaml_root()
	{
		var view = BuiltView();
		view.ShouldBeOfType<SampleView>();
		view.Root.Orientation.ShouldBe(Orientation.Vertical);
		view.Root.Children.Count.ShouldBe(2);
	}

	[Test]
	public void Named_elements_become_typed_fields_with_their_literal_properties()
	{
		var view = BuiltView();

		view.Title.ShouldBeOfType<TextBlock>();
		view.Title.Text.ShouldBe("Hello, SUIsei");
		view.Title.FontSize.ShouldBe(24f);

		view.Ok.ShouldBeOfType<Button>();
		view.OkLabel.Text.ShouldBe("OK");
	}

	[Test]
	public void Containers_are_wired_from_the_xaml_nesting()
	{
		var view = BuiltView();

		// StackPanel hosts the two children in document order (the Children container model).
		view.Root.Children[0].ShouldBe(view.Title);
		view.Root.Children[1].ShouldBe(view.Ok);

		// Button hosts its single child via the Content container model.
		view.Ok.Content.ShouldBe(view.OkLabel);
	}

	[Test]
	public void The_generated_tree_lays_out_and_composites_through_a_uiroot()
	{
		var view = BuiltView();
		var root = new UIRoot(UISurface.Hud) { Content = view.Root };
		root.LayoutPass(new Size(200f, 100f));

		// The stack arranges to the surface, and the Button's default background composites to a fill.
		view.Root.LayoutRect.Width.ShouldBe(200f);
		view.Title.DesiredSize.Height.ShouldBeGreaterThan(0f);
		root.Render().Commands.Length.ShouldBeGreaterThan(0);
	}
}
