using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>The data item the templated list binds against (resolved by <c>x:DataType</c> at generate time).</summary>
public sealed class Row
{
	public string Label { get; init; } = "";
}

/// <summary>Code-behind half of <c>Sample/TemplatedView.xaml</c>; the generator emits the matching partial.</summary>
public partial class TemplatedView;

/// <summary>
/// End-to-end proof that the generated template factories build real, working trees: the
/// fixture's compilation already proves the emitted C# is valid; these tests prove the runtime behaviour — a
/// ControlTemplate's <c>{TemplateBinding Content}</c> flows the control's content into its
/// <see cref="ContentPresenter"/>, and an <see cref="ItemsControl"/>'s <c>DataTemplate</c> realizes one bound
/// container per item.
/// </summary>
public sealed class TemplatedViewTests
{
	private static TemplatedView Built()
	{
		var view = new TemplatedView();
		view.InitializeComponent();
		return view;
	}

	[Test]
	public void Control_template_hosts_a_border_with_a_content_presenter()
	{
		var view = Built();
		var root = view.Card.GetTemplateRoot();

		ShouldBeTestExtensions.ShouldBeOfType<Border>(root);
		((Border)root!).Child.ShouldBeOfType<ContentPresenter>();
	}

	[Test]
	public void Template_binding_flows_the_controls_content_into_the_presenter()
	{
		var view = Built();
		view.Card.Content = "Hello";

		var presenter = (ContentPresenter)((Border)view.Card.GetTemplateRoot()!).Child!;
		presenter.Content.ShouldBe("Hello"); // {TemplateBinding Content}, kept live
	}

	[Test]
	public void A_generated_template_binding_yields_to_a_local_value_on_the_part()
	{
		var view = Built();
		view.Card.Opacity = 0.5f;
		var border = (Border)view.Card.GetTemplateRoot()!;
		border.Opacity.ShouldBe(0.5f); // {TemplateBinding Opacity}, on the part's Template rung

		border.Opacity = 0.9f;
		view.Card.Opacity = 0.3f;
		border.Opacity.ShouldBe(0.9f); // the app's Local value wins and the template's push does not overwrite it
	}

	[Test]
	public void Items_control_realizes_one_bound_container_per_item()
	{
		var view = Built();
		view.List.ItemsSource = new[] { new Row { Label = "Alpha" }, new Row { Label = "Beta" } };

		var host = ShouldBeNullExtensions.ShouldNotBeNull<Panel>(view.List.ItemsHost);
		host.Children.Count.ShouldBe(2);
		((TextBlock)host.Children[0]).Text.ShouldBe("Alpha"); // {x:Bind Label} resolved against the item
		((TextBlock)host.Children[1]).Text.ShouldBe("Beta");
	}

	[Test]
	public void The_templated_view_lays_out_and_composites_through_a_uiroot()
	{
		var view = Built();
		view.Card.Content = "Card";
		view.List.ItemsSource = new[] { new Row { Label = "One" } };

		var root = new UIRoot(UISurface.Hud) { Content = view.Root };
		root.LayoutPass(new Size(200f, 120f));

		root.Render().Commands.Length.ShouldBeGreaterThan(0);
	}
}
