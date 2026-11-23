using System.Numerics;

using Vysara.Animation;
using Vysara.Controls;
using Vysara.Extensions;
using Vysara.Tests.Sample;

namespace Vysara.Tests;

/// <summary>
/// End to end over the build-time-generated <c>Sample/MarkupFormsView.xaml</c> and <c>Sample/ThemedView.xaml</c>: the
/// tree each builds, which proves the XAML namespaces, property elements, value converters and value markup reach the
/// runtime as the values they name — not just text the generator accepts.
/// </summary>
public sealed class MarkupFormsTests
{
	private static MarkupFormsView Build()
	{
		var view = new MarkupFormsView();
		view.InitializeComponent();
		return view;
	}

	[Test]
	public void Design_time_markup_is_left_out()
	{
		var view = Build();

		// Title, Card, List, the Canvas, Caption, Clipped, Scroller, Rows and Badge: the d:DesignNote element is not built.
		view.Root.Children.Count.ShouldBe(9);
		view.Title.Text.ShouldBe("Forms");
	}

	[Test]
	public void Static_resources_resolve_through_markup_ancestors_and_merged_dictionaries()
	{
		var view = Build();

		view.Title.Style.ShouldNotBeNull();
		view.Title.FontSize.ShouldBe(18f);   // from the merged dictionary
		view.Card.Style.ShouldNotBeNull();
		TestGlow.GetRadius(view.Card).ShouldBe(4f);   // an attached-property Setter in the extension namespace
		Canvas.GetLeft(view.Card).ShouldBe(12f);      // an attached-property Setter with a <Setter.Value>
	}

	[Test]
	public void Property_elements_set_values_objects_collections_and_attached_properties()
	{
		var view = Build();

		view.Card.Padding.ShouldBe(new Thickness(4f, 8f));
		view.Card.Child.ShouldBeSameAs(view.CardText);
		view.List.Children.Count.ShouldBe(2);
		Canvas.GetLeft(view.Pinned).ShouldBe(30f);
		TestGlow.GetRadius(view.Pinned).ShouldBe(1.5f);
		view.Badge.Content.ShouldBeOfType<TextBlock>().Text.ShouldBe("Badge");
	}

	[Test]
	public void Converters_and_value_markup_produce_typed_values()
	{
		var view = Build();

		view.Card.Background.SolidColor().ShouldBe(HdrColor.FromRgb(100, 149, 237)); // CornflowerBlue, as a brush
		view.CardText.Text.ShouldBe(SampleStrings.CardTitle);              // {x:Static} const
		view.Pinned.Background.ShouldBeSameAs(SampleStrings.Accent);      // {x:Static} static readonly field
		view.Caption.ShouldBeOfType<SampleCaption>();                     // a using: element
		view.Badge.Anchor.ShouldBe(new Point(10f, 20f));
		view.Badge.Scale.ShouldBe(new Vector2(2f, 3f));
		view.Badge.Offset.ShouldBe(new Vector3(1f, 2f, 3f));
		view.Badge.Tint.ShouldBe(HdrColor.FromRgb(255, 0, 0, 128));
		view.Badge.Halo.ShouldBeNull();                                   // {x:Null}
		TestGlow.GetRadius(view.Badge).ShouldBe(2.5f);
	}

	[Test]
	public void A_storyboard_animates_an_attached_property_through_an_extension_owner()
	{
		var view = Build();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;

		VisualStateManager.GoToState(view.Badge, "On", useTransitions: true).ShouldBeTrue();
		host.Tick(0.5, 0f, new Vector2(400f, 300f));

		// No From: halfway from the current 2.5 to 10.
		TestGlow.GetRadius(view.Badge).ShouldBe(6.25f, 1e-4f);
	}

	[Test]
	public void The_runtimes_extensions_are_set_from_markup()
	{
		var view = Build();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0.016, 0f, new Vector2(400f, 600f));

		Clipping.GetClipToBounds(view.Clipped).ShouldBeTrue();
		view.Clipped.Visual.Clip.ShouldBe(new Rect(0f, 0f, 20f, 10f));
		Scrolling.GetWheelScrollAmount(view.Scroller).ShouldBe(64f);
		// Set on the panel the ItemsPanelTemplate builds, not dropped with the rest of its attributes.
		Virtualization.GetRowHeight(view.Rows.ItemsHost.ShouldBeOfType<VirtualizingStackPanel>()).ShouldBe(32f);
	}

	[Test]
	public void An_unsealed_root_rebuilds_its_merged_dictionaries_in_place()
	{
		var view = new ThemedView();
		view.InitializeComponent();
		view.Heading.FontSize.ShouldBe(24f);    // BasedOn a style in the merged dictionary
		view.Heading.Opacity.ShouldBe(0.5f);

		var before = view.Heading;
		view.RebuildComponent();

		view.Resources!.MergedDictionaries.Count.ShouldBe(1);
		view.Heading.ShouldNotBeSameAs(before);
		view.Heading.FontSize.ShouldBe(24f);
		view.Heading.Opacity.ShouldBe(0.5f);
	}
}
