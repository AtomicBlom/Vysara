using SUIsei.Controls;
using SUIsei.Tests.Generated;

namespace SUIsei.Tests;

/// <summary>
/// <c>[ContentProperty]</c> (PlanningUI.md MU6): child elements route into the property the control declares,
/// appended for a collection (Panel.Children) and assigned for a scalar (ScrollViewer.Content, Border.Child) —
/// driven by the attribute, not a hardcoded name. <c>ContentModelView.xaml</c> nests all three.
/// </summary>
public sealed class ContentPropertyTests
{
	[Test]
	public void Content_models_route_via_the_declared_property()
	{
		var view = new ContentModelView();
		view.InitializeComponent();

		// ScrollViewer.Content — a scalar content property.
		view.Root.ShouldBeOfType<ScrollViewer>();
		view.Root.Content.ShouldBeSameAs(view.Stack);

		// Panel.Children — a collection content property (children appended in order).
		view.Stack.Children.Count.ShouldBe(2);
		view.Stack.Children[0].ShouldBeSameAs(view.Box);
		view.Stack.Children[1].ShouldBeSameAs(view.Second);

		// Border.Child — a scalar content property.
		view.Box.Child.ShouldBeSameAs(view.Label);
	}
}
