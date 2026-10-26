using Microsoft.CodeAnalysis;

namespace SUIsei.Tests;

/// <summary>
/// Generator coverage for the XML namespaces, generic property elements, ignorable markup, the value converters,
/// <c>{x:Null}</c> / <c>{x:Static}</c>, merged dictionaries and attached properties: the code each form emits, compiled
/// against the SUIsei runtime, and the error each malformed form reports at its XAML line.
/// </summary>
public sealed class XamlMarkupGeneratorTests
{
	// Test-only types: members of the extension namespace (SUIsei.Extensions), types in the default namespace's other
	// CLR namespaces, and a using: namespace holding static members for {x:Static}.
	private const string CodeBehind = """
		namespace SUIsei.Extensions
		{
			public sealed class Glow : SUIsei.DependencyObject
			{
				public static readonly SUIsei.DependencyProperty<Glow, float> RadiusProperty =
					SUIsei.DependencyProperty.RegisterAttached<Glow, float>("Radius", new SUIsei.PropertyMetadata<Glow, float>());
				public static void SetRadius(SUIsei.UIElement element, float value) => element.SetValue(RadiusProperty, value);
				public static float GetRadius(SUIsei.UIElement element) => element.GetValue(RadiusProperty);
			}
			public sealed class Badge : SUIsei.Controls.ContentControl
			{
				public SUIsei.Point Anchor { get; set; }
				public System.Numerics.Vector2 Scale { get; set; }
				public System.Numerics.Vector3 Offset { get; set; }
				public SUIsei.HdrColor? Halo { get; set; }
			}
		}
		namespace SUIsei.Shapes { public sealed class Blob : SUIsei.Controls.ContentControl { } }
		namespace SUIsei.Media { public sealed class Swatch : SUIsei.Controls.ContentControl { } }
		namespace SUIsei.Documents { public sealed class Page : SUIsei.Controls.ContentControl { } }
		namespace Demo
		{
			public partial class V { }
			public sealed class Caption : SUIsei.Controls.ContentControl { }
			public static class Palette
			{
				public const string Title = "Title";
				public static readonly SUIsei.HdrColor Accent = SUIsei.HdrColor.FromRgb(1, 2, 3);
				public static float Gap => 6f;
			}
		}
		""";

	// A view with `body` inside a StackPanel root that declares every XML namespace the forms use.
	private static string View(string body) => $$"""
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            xmlns:suisei="https://github.com/AtomicBlom/SUIsei/extensions"
		            xmlns:local="using:Demo"
		            xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
		            xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
		            xmlns:f="urn:foreign"
		            mc:Ignorable="d"
		            x:Class="Demo.V">
		  {{body}}
		</StackPanel>
		""";

	// The XAML line (zero-based) the body's first line lands on in View.
	private const int BodyLine = 9;

	private static int Line(Diagnostic diagnostic) => diagnostic.Location.GetLineSpan().StartLinePosition.Line;

	// Generate `body`, expect no diagnostics, check that the emitted code compiles, and return it.
	private static string Generates(string body)
	{
		var xaml = View(body);
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		result.Diagnostics.ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
		return result.AllGeneratedText();
	}

	// Generate `body` and return its one diagnostic, which must be an error.
	private static Diagnostic Fails(string body)
	{
		var diagnostic = XamlGeneratorHarness.Run(View(body), CodeBehind).Diagnostics.ShouldHaveSingleItem();
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		return diagnostic;
	}

	// --- XML namespaces ---

	[Test]
	[Arguments("<Blob />", "new SUIsei.Shapes.Blob()")]
	[Arguments("<Swatch />", "new SUIsei.Media.Swatch()")]
	[Arguments("<Page />", "new SUIsei.Documents.Page()")]
	[Arguments("<Border />", "new SUIsei.Controls.Border()")]
	[Arguments("<local:Caption />", "new Demo.Caption()")]
	[Arguments("<suisei:Badge />", "new SUIsei.Extensions.Badge()")]
	public void Elements_resolve_in_their_XML_namespace(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<suisei:Nope />", "Nope")]
	[Arguments("<Border suisei:Glow.Bogus=\"1\" />", "Glow.Bogus")]
	[Arguments("<Border suisei:Nope.Radius=\"1\" />", "Nope.Radius")]
	[Arguments("<Border><suisei:Nope.Radius>1</suisei:Nope.Radius></Border>", "Nope.Radius")]
	public void An_unknown_extension_member_is_SUISEI022_at_its_line(string body, string member)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("SUISEI022");
		diagnostic.GetMessage().ShouldContain($"'{member}'");
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	public void An_element_in_a_foreign_namespace_is_unknown_and_a_foreign_attribute_is_ignored_with_a_warning()
	{
		Fails("<f:Widget />").Id.ShouldBe("SUISEI003");

		var warning = XamlGeneratorHarness.Run(View("<Border f:Tag=\"x\" />"), CodeBehind).Diagnostics.ShouldHaveSingleItem();
		warning.Id.ShouldBe("SUISEI013");
		warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
	}

	// --- Ignorable (design-time) markup ---

	[Test]
	public void Ignorable_markup_is_skipped()
	{
		var code = Generates("""
			<d:DesignNote Text="designer only" />
			<Border d:Background="Red" Width="10">
			  <d:Border.Padding>4</d:Border.Padding>
			  <Border.Child>
			    <TextBlock d:Text="design" Text="real" />
			  </Border.Child>
			</Border>
			""");
		code.ShouldNotContain("DesignNote");
		code.ShouldNotContain("design\"");
		code.ShouldNotContain("Padding");
		code.ShouldContain(".Text = \"real\";");
	}

	// --- Property elements ---

	[Test]
	[Arguments("<Border><Border.Padding>1,2</Border.Padding></Border>", ".Padding = new global::SUIsei.Thickness(1f, 2f);")]
	[Arguments("<Border><FrameworkElement.Margin>3</FrameworkElement.Margin></Border>", ".Margin = new global::SUIsei.Thickness(3f);")]
	[Arguments("<Border><Border.Background>Gold</Border.Background></Border>", ".Background = global::SUIsei.HdrColor.FromRgb(255, 215, 0);")]
	[Arguments("<Border><Border.Child><TextBlock x:Name=\"Inner\" /></Border.Child></Border>", ".Child = Inner;")]
	[Arguments("<StackPanel x:Name=\"P\"><StackPanel.Children><TextBlock x:Name=\"A\" /><TextBlock x:Name=\"B\" /></StackPanel.Children></StackPanel>", "P.Children.Add(B);")]
	[Arguments("<suisei:Badge><suisei:Badge.Content><TextBlock x:Name=\"C\" /></suisei:Badge.Content></suisei:Badge>", ".Content = C;")]
	[Arguments("<Canvas><Border x:Name=\"Pin\"><Canvas.Left>5</Canvas.Left></Border></Canvas>", "global::SUIsei.Controls.Canvas.SetLeft(Pin, 5f);")]
	[Arguments("<Border x:Name=\"G\"><suisei:Glow.Radius>2</suisei:Glow.Radius></Border>", "global::SUIsei.Extensions.Glow.SetRadius(G, 2f);")]
	public void Property_elements_set_any_property(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<Border><Border.Bogus>1</Border.Bogus></Border>", "SUISEI004")]
	[Arguments("<Border><Nope.Prop>1</Nope.Prop></Border>", "SUISEI003")]
	[Arguments("<Border><Canvas.Bogus>1</Canvas.Bogus></Border>", "SUISEI004")]
	[Arguments("<Border><Border.Padding /></Border>", "SUISEI020")]
	[Arguments("<Border><Border.Child>text<TextBlock /></Border.Child></Border>", "SUISEI020")]
	[Arguments("<Border><Border.Child><TextBlock /><TextBlock /></Border.Child></Border>", "SUISEI020")]
	[Arguments("<Border><Border.Padding>1,2,3</Border.Padding></Border>", "SUISEI012")]
	public void A_bad_property_element_is_an_error_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Value converters ---

	[Test]
	[Arguments("<suisei:Badge Anchor=\"10,20\" />", ".Anchor = new global::SUIsei.Point(10f, 20f);")]
	[Arguments("<suisei:Badge Anchor=\" 1.5  -2 \" />", ".Anchor = new global::SUIsei.Point(1.5f, -2f);")]
	[Arguments("<suisei:Badge Scale=\"2 3\" />", ".Scale = new global::System.Numerics.Vector2(2f, 3f);")]
	[Arguments("<suisei:Badge Offset=\"1,2,3\" />", ".Offset = new global::System.Numerics.Vector3(1f, 2f, 3f);")]
	[Arguments("<Border Background=\"cornflowerblue\" />", ".Background = global::SUIsei.HdrColor.FromRgb(100, 149, 237);")]
	[Arguments("<Border Background=\"Transparent\" />", ".Background = global::SUIsei.HdrColor.FromRgb(255, 255, 255, 0);")]
	[Arguments("<Border Background=\"#80FF0000\" />", ".Background = global::SUIsei.HdrColor.FromRgb(255, 0, 0, 128);")]
	[Arguments("<Border Padding=\"1,2,3,4\" />", ".Padding = new global::SUIsei.Thickness(1f, 2f, 3f, 4f);")]
	[Arguments("<Border Width=\"5.\" />", ".Width = 5f;")]
	[Arguments("<Border HorizontalAlignment=\"Left\" />", ".HorizontalAlignment = global::SUIsei.HorizontalAlignment.Left;")]
	public void Values_convert_for_their_type(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<suisei:Badge Anchor=\"10\" />", "two numbers")]
	[Arguments("<suisei:Badge Anchor=\"1,2,3\" />", "two numbers")]
	[Arguments("<suisei:Badge Scale=\"a,b\" />", "two numbers")]
	[Arguments("<suisei:Badge Offset=\"1,2\" />", "three numbers")]
	[Arguments("<suisei:Badge Anchor=\"1e40,1\" />", "two numbers")]
	[Arguments("<Border Background=\"Reddish\" />", "named colour")]
	[Arguments("<Border Padding=\"1,2,3\" />", "one, two or four numbers")]
	[Arguments("<Border Width=\"NaN\" />", "a number")]
	[Arguments("<Border Width=\"1e40\" />", "a number")]
	[Arguments("<Border HorizontalAlignment=\"Leftish\" />", "a member of HorizontalAlignment")]
	public void A_bad_value_is_SUISEI012_naming_the_syntax(string body, string syntax)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("SUISEI012");
		diagnostic.GetMessage().ShouldContain(syntax);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- {x:Null}, {x:Static} ---

	[Test]
	[Arguments("<suisei:Badge Halo=\"{x:Null}\" />", ".Halo = null;")]
	[Arguments("<TextBlock Text=\"{x:Null}\" />", ".Text = null;")]
	[Arguments("<TextBlock Text=\"{x:Static local:Palette.Title}\" />", ".Text = global::Demo.Palette.Title;")]
	[Arguments("<Border Background=\"{x:Static local:Palette.Accent}\" />", ".Background = global::Demo.Palette.Accent;")]
	[Arguments("<Border Width=\"{x:Static local:Palette.Gap}\" />", ".Width = global::Demo.Palette.Gap;")]
	[Arguments("<Border HorizontalAlignment=\"{x:Static HorizontalAlignment.Right}\" />", ".HorizontalAlignment = global::SUIsei.HorizontalAlignment.Right;")]
	[Arguments("<Border Background=\"{x:Static Demo.Palette.Accent}\" />", ".Background = global::Demo.Palette.Accent;")]
	[Arguments("<Canvas><Border x:Name=\"S\" Canvas.Left=\"{x:Static local:Palette.Gap}\" /></Canvas>", "global::SUIsei.Controls.Canvas.SetLeft(S, global::Demo.Palette.Gap);")]
	public void Value_markup_resolves_at_compile_time(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<Border Width=\"{x:Null}\" />", "SUISEI012")]
	[Arguments("<Border Width=\"{x:Static local:Palette.Nope}\" />", "SUISEI017")]
	[Arguments("<Border Width=\"{x:Static Nope.Member}\" />", "SUISEI017")]
	[Arguments("<Border Width=\"{x:Static local:Palette.Title}\" />", "SUISEI012")]
	public void Bad_value_markup_is_an_error_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Attached properties ---

	[Test]
	[Arguments("<Grid><Border Grid.Row=\"x\" /></Grid>", "SUISEI012")]
	[Arguments("<Grid><Border Grid.Bogus=\"1\" /></Grid>", "SUISEI004")]
	[Arguments("<Border Nope.Row=\"1\" />", "SUISEI003")]
	[Arguments("<Border suisei:Glow.Radius=\"wide\" />", "SUISEI012")]
	public void A_bad_attached_property_is_an_error_not_a_warning(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Resources ---

	[Test]
	public void Merged_dictionaries_and_attached_setters_emit_and_compile()
	{
		var code = Generates("""
			<StackPanel.Resources>
			  <ResourceDictionary>
			    <ResourceDictionary.MergedDictionaries>
			      <ResourceDictionary>
			        <Style x:Key="Base" TargetType="TextBlock"><Setter Property="FontSize" Value="18" /></Style>
			      </ResourceDictionary>
			    </ResourceDictionary.MergedDictionaries>
			    <Style x:Key="Derived" TargetType="TextBlock" BasedOn="{StaticResource Base}">
			      <Setter Property="Canvas.Left" Value="3" />
			      <Setter Property="suisei:Glow.Radius"><Setter.Value>2</Setter.Value></Setter>
			    </Style>
			  </ResourceDictionary>
			</StackPanel.Resources>
			<Border><TextBlock x:Name="T" Style="{StaticResource Derived}" /></Border>
			""");
		code.ShouldContain("Root.Resources.MergedDictionaries.Clear();");
		code.ShouldContain("Root.Resources.MergedDictionaries.Add(__merged");
		code.ShouldContain("Property = global::SUIsei.Controls.Canvas.LeftProperty, Value = 3f");
		code.ShouldContain("Property = global::SUIsei.Extensions.Glow.RadiusProperty, Value = 2f");
		// The lookup reaches the markup ancestor that declared the resources: nothing is parented yet.
		code.ShouldContain("T.Style = (T.FindResource(\"Derived\") ?? Root.FindResource(\"Derived\")) as global::SUIsei.Style;");
	}

	[Test]
	[Arguments("<StackPanel.Resources><ResourceDictionary Source=\"Theme.xaml\" /></StackPanel.Resources>", "SUISEI020")]
	[Arguments("<StackPanel.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><Style x:Key=\"S\" TargetType=\"Border\" /></ResourceDictionary.MergedDictionaries></ResourceDictionary></StackPanel.Resources>", "SUISEI020")]
	[Arguments("<StackPanel.Resources><Style x:Key=\"S\" TargetType=\"Border\"><Setter Property=\"Canvas.Bogus\" Value=\"1\" /></Style></StackPanel.Resources>", "SUISEI018")]
	[Arguments("<StackPanel.Resources><Style x:Key=\"S\" TargetType=\"Border\" BasedOn=\"Base\" /></StackPanel.Resources>", "SUISEI007")]
	public void A_bad_resource_form_is_an_error(string body, string id) =>
		Fails(body).Id.ShouldBe(id);
}
