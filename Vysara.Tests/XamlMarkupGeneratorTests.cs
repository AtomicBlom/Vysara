using Microsoft.CodeAnalysis;

namespace Vysara.Tests;

/// <summary>
/// Generator coverage for the XML namespaces, generic property elements, ignorable markup, the value converters,
/// <c>{x:Null}</c> / <c>{x:Static}</c>, merged dictionaries and attached properties: the code each form emits, compiled
/// against the Vysara runtime, and the error each malformed form reports at its XAML line.
/// </summary>
public sealed class XamlMarkupGeneratorTests
{
	// Test-only types: members of the extension namespace (Vysara.Extensions), types in the default namespace's other
	// CLR namespaces, and a using: namespace holding static members for {x:Static}.
	private const string CodeBehind = """
		namespace Vysara.Extensions
		{
			public sealed class Glow : Vysara.DependencyObject
			{
				public static readonly Vysara.DependencyProperty<Glow, float> RadiusProperty =
					Vysara.DependencyProperty.RegisterAttached<Glow, float>("Radius", new Vysara.PropertyMetadata<Glow, float>());
				public static void SetRadius(Vysara.UIElement element, float value) => element.SetValue(RadiusProperty, value);
				public static float GetRadius(Vysara.UIElement element) => element.GetValue(RadiusProperty);
			}
			public sealed class Badge : Vysara.Controls.ContentControl
			{
				public Vysara.Point Anchor { get; set; }
				public System.Numerics.Vector2 Scale { get; set; }
				public System.Numerics.Vector3 Offset { get; set; }
				public Vysara.HdrColor? Halo { get; set; }
			}
		}
		namespace Vysara.Shapes { public sealed class Blob : Vysara.Controls.ContentControl { } }
		namespace Vysara.Media { public sealed class Swatch : Vysara.Controls.ContentControl { } }
		namespace Vysara.Documents { public sealed class Page : Vysara.Controls.ContentControl { } }
		namespace Demo
		{
			public partial class V
			{
				public string? Caption { get; set; }
				private void OnRouted(object sender, Vysara.RoutedEvents.RoutedEventArgs e) { }
				private void OnPlain(object sender, System.EventArgs e) { }
				private void OnSelection(object sender, Vysara.Controls.SelectionChangedEventArgs e) { }
				private void OnAny<T>(object sender, T e) { }
				private static void OnStatic(object sender, Vysara.RoutedEvents.RoutedEventArgs e) { }
				private void OnByRef(object sender, ref Vysara.RoutedEvents.RoutedEventArgs e) { }
			}
			public sealed class Caption : Vysara.Controls.ContentControl { }
			public struct Pair { public float A { get; set; } }
			public sealed class Hidden : Vysara.Controls.ContentControl { internal Hidden() { } }
			public sealed class Closed : Vysara.Controls.ContentControl { private Closed() { } }
			public sealed class Gate : Vysara.Controls.ContentControl
			{
				public float Shut { get; private set; }
				public float Built { get; init; }
				public float Inside { get; internal set; }
			}
			public class Level : Vysara.Controls.ContentControl { public float Depth { get; protected set; } }
			public static class Palette
			{
				public const string Title = "Title";
				public static readonly Vysara.Media.Brush Accent = new Vysara.Media.SolidColorBrush(Vysara.HdrColor.FromRgb(1, 2, 3));
				public static readonly Vysara.HdrColor Ink = Vysara.HdrColor.FromRgb(1, 2, 3);
				public static float Gap => 6f;
			}
		}
		""";

	// A view with `body` inside a StackPanel root that declares every XML namespace the forms use.
	private static string View(string body) => $$"""
		<StackPanel xmlns="https://github.com/AtomicBlom/Vysara/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            xmlns:vysara="https://github.com/AtomicBlom/Vysara/extensions"
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
	[Arguments("<Blob />", "new Vysara.Shapes.Blob()")]
	[Arguments("<Swatch />", "new Vysara.Media.Swatch()")]
	[Arguments("<Page />", "new Vysara.Documents.Page()")]
	[Arguments("<Border />", "new Vysara.Controls.Border()")]
	[Arguments("<local:Caption />", "new Demo.Caption()")]
	[Arguments("<vysara:Badge />", "new Vysara.Extensions.Badge()")]
	public void Elements_resolve_in_their_XML_namespace(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	// --- Events ---

	/// <summary>A handler binds to an event as C# binds it to the event's delegate: a routed event's args, an
	/// EventHandler's, and a narrower event's args taken by a handler of their base.</summary>
	[Test]
	[Arguments("<Button Click=\"OnRouted\" />", ".Click += this.OnRouted;")]
	[Arguments("<Slider ValueChanged=\"OnPlain\" />", ".ValueChanged += this.OnPlain;")]
	[Arguments("<ListView SelectionChanged=\"OnSelection\" />", ".SelectionChanged += this.OnSelection;")]
	[Arguments("<ListView SelectionChanged=\"OnRouted\" />", ".SelectionChanged += this.OnRouted;")]
	[Arguments("<Button Click=\"OnAny\" />", ".Click += this.OnAny;")]
	public void Event_handlers_bind_to_the_events_own_delegate(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<Button Click=\"OnPlain\" />", "'OnPlain(object, RoutedEventArgs)' for event 'Click'")]
	[Arguments("<ListView SelectionChanged=\"OnMissing\" />", "'OnMissing(object, SelectionChangedEventArgs)' for event 'SelectionChanged'")]
	[Arguments("<Button Click=\"OnStatic\" />", "'OnStatic(object, RoutedEventArgs)' for event 'Click'")]
	[Arguments("<Button Click=\"OnByRef\" />", "'OnByRef(object, RoutedEventArgs)' for event 'Click'")]
	public void A_handler_the_event_cannot_bind_to_is_reported(string body, string message)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("VYSARA010");
		diagnostic.GetMessage().ShouldContain(message);
	}

	[Test]
	[Arguments("<vysara:Nope />", "Nope")]
	[Arguments("<Border vysara:Glow.Bogus=\"1\" />", "Glow.Bogus")]
	[Arguments("<Border vysara:Nope.Radius=\"1\" />", "Nope.Radius")]
	[Arguments("<Border><vysara:Nope.Radius>1</vysara:Nope.Radius></Border>", "Nope.Radius")]
	public void An_unknown_extension_member_is_VYSARA022_at_its_line(string body, string member)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("VYSARA022");
		diagnostic.GetMessage().ShouldContain($"'{member}'");
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	public void The_stroke_extensions_set_on_a_shape()
	{
		var code = Generates("""<Path Data="M 0,0 L 10,0" Stroke="Red" vysara:StrokeTrim.End="0.5" vysara:StrokeBrushMapping.Mode="AlongLength" />""");
		code.ShouldContain("global::Vysara.Extensions.StrokeTrim.SetEnd(");
		code.ShouldContain("global::Vysara.Extensions.StrokeBrushMappingMode.AlongLength");
	}

	[Test]
	public void Typography_NumeralAlignment_is_WinUI_s_attached_property_on_a_text_block()
	{
		var code = Generates("""<TextBlock Text="12" Typography.NumeralAlignment="Tabular" />""");
		code.ShouldContain("global::Vysara.Documents.Typography.SetNumeralAlignment(");
		code.ShouldContain("global::Vysara.FontNumeralAlignment.Tabular");
		Generates("""<TextBlock Text="12"><Typography.NumeralAlignment>Proportional</Typography.NumeralAlignment></TextBlock>""")
			.ShouldContain("global::Vysara.FontNumeralAlignment.Proportional");
	}

	[Test]
	[Arguments("<Border Typography.NumeralAlignment=\"Tabular\" />", "VYSARA004")]
	[Arguments("<TextBlock Typography.NumeralAlignment=\"Wide\" />", "VYSARA012")]
	public void Typography_NumeralAlignment_off_a_text_block_or_with_an_unknown_value_is_reported_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	[Arguments("<Border vysara:StrokeTrim.End=\"0.5\" />")]
	[Arguments("<Border><vysara:StrokeTrim.End>0.5</vysara:StrokeTrim.End></Border>")]
	public void An_attached_property_on_an_element_its_setter_does_not_take_is_VYSARA004_at_its_line(string body)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("VYSARA004");
		diagnostic.GetMessage().ShouldContain("'StrokeTrim.End'");
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	public void The_text_effects_set_on_a_text_block()
	{
		var code = Generates("""<TextBlock Text="Go" vysara:TextEffects.OutlineThickness="0.08" vysara:TextEffects.OutlineBrush="Black" vysara:TextEffects.ShadowOffset="0.05,0.1" vysara:TextEffects.ShadowSoftness="0.2" vysara:TextEffects.ShadowBrush="#80000000" />""");
		code.ShouldContain("global::Vysara.Extensions.TextEffects.SetOutlineThickness(");
		code.ShouldContain("global::Vysara.Extensions.TextEffects.SetOutlineBrush(");
		code.ShouldContain("global::Vysara.Extensions.TextEffects.SetShadowOffset(");
		code.ShouldContain("global::Vysara.Extensions.TextEffects.SetShadowSoftness(");
		code.ShouldContain("global::Vysara.Extensions.TextEffects.SetShadowBrush(");
	}

	[Test]
	[Arguments("<Border vysara:TextEffects.OutlineThickness=\"0.1\" />", "VYSARA004")]
	[Arguments("<TextBox vysara:TextEffects.ShadowBrush=\"Black\" />", "VYSARA004")]
	[Arguments("<TextBlock vysara:TextEffects.ShadowOffset=\"near\" />", "VYSARA012")]
	public void A_text_effect_off_a_text_block_or_with_a_value_that_does_not_convert_is_reported_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	public void An_element_in_a_foreign_namespace_is_unknown_and_a_foreign_attribute_is_ignored_with_a_warning()
	{
		Fails("<f:Widget />").Id.ShouldBe("VYSARA003");

		var warning = XamlGeneratorHarness.Run(View("<Border f:Tag=\"x\" />"), CodeBehind).Diagnostics.ShouldHaveSingleItem();
		warning.Id.ShouldBe("VYSARA013");
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
	[Arguments("<Border><Border.Padding>1,2</Border.Padding></Border>", ".Padding = new global::Vysara.Thickness(1f, 2f);")]
	[Arguments("<Border><FrameworkElement.Margin>3</FrameworkElement.Margin></Border>", ".Margin = new global::Vysara.Thickness(3f);")]
	[Arguments("<Border><Border.Background>Gold</Border.Background></Border>", ".Background = new global::Vysara.Media.SolidColorBrush(global::Vysara.HdrColor.FromRgb(255, 215, 0));")]
	[Arguments("<Border><Border.Child><TextBlock x:Name=\"Inner\" /></Border.Child></Border>", ".Child = Inner;")]
	[Arguments("<StackPanel x:Name=\"P\"><StackPanel.Children><TextBlock x:Name=\"A\" /><TextBlock x:Name=\"B\" /></StackPanel.Children></StackPanel>", "P.Children.Add(B);")]
	[Arguments("<vysara:Badge><vysara:Badge.Content><TextBlock x:Name=\"C\" /></vysara:Badge.Content></vysara:Badge>", ".Content = C;")]
	[Arguments("<Canvas><Border x:Name=\"Pin\"><Canvas.Left>5</Canvas.Left></Border></Canvas>", "global::Vysara.Controls.Canvas.SetLeft(Pin, 5f);")]
	[Arguments("<Border x:Name=\"G\"><vysara:Glow.Radius>2</vysara:Glow.Radius></Border>", "global::Vysara.Extensions.Glow.SetRadius(G, 2f);")]
	public void Property_elements_set_any_property(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<Border><Border.Bogus>1</Border.Bogus></Border>", "VYSARA004")]
	[Arguments("<Border><Nope.Prop>1</Nope.Prop></Border>", "VYSARA003")]
	[Arguments("<Border><Canvas.Bogus>1</Canvas.Bogus></Border>", "VYSARA004")]
	[Arguments("<Border><Border.Padding /></Border>", "VYSARA020")]
	[Arguments("<Border><Border.Child>text<TextBlock /></Border.Child></Border>", "VYSARA020")]
	[Arguments("<Border><Border.Child><TextBlock /><TextBlock /></Border.Child></Border>", "VYSARA020")]
	[Arguments("<Border><Border.Padding>1,2,3</Border.Padding></Border>", "VYSARA012")]
	public void A_bad_property_element_is_an_error_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Value converters ---

	[Test]
	[Arguments("<vysara:Badge Anchor=\"10,20\" />", ".Anchor = new global::Vysara.Point(10f, 20f);")]
	[Arguments("<vysara:Badge Anchor=\" 1.5  -2 \" />", ".Anchor = new global::Vysara.Point(1.5f, -2f);")]
	[Arguments("<vysara:Badge Scale=\"2 3\" />", ".Scale = new global::System.Numerics.Vector2(2f, 3f);")]
	[Arguments("<vysara:Badge Offset=\"1,2,3\" />", ".Offset = new global::System.Numerics.Vector3(1f, 2f, 3f);")]
	[Arguments("<Border Background=\"cornflowerblue\" />", ".Background = new global::Vysara.Media.SolidColorBrush(global::Vysara.HdrColor.FromRgb(100, 149, 237));")]
	[Arguments("<Border Background=\"Transparent\" />", ".Background = new global::Vysara.Media.SolidColorBrush(global::Vysara.HdrColor.FromRgb(255, 255, 255, 0));")]
	[Arguments("<Border Background=\"#80FF0000\" />", ".Background = new global::Vysara.Media.SolidColorBrush(global::Vysara.HdrColor.FromRgb(255, 0, 0, 128));")]
	[Arguments("<Border Padding=\"1,2,3,4\" />", ".Padding = new global::Vysara.Thickness(1f, 2f, 3f, 4f);")]
	[Arguments("<Border CornerRadius=\"4\" />", ".CornerRadius = new global::Vysara.CornerRadius(4f);")]
	[Arguments("<Border CornerRadius=\"1,2,3,4\" />", ".CornerRadius = new global::Vysara.CornerRadius(1f, 2f, 3f, 4f);")]
	[Arguments("<Border RenderTransformOrigin=\"0.5,0.5\" />", ".RenderTransformOrigin = new global::Vysara.Point(0.5f, 0.5f);")]
	[Arguments("<Border><Border.RenderTransform><MatrixTransform Matrix=\"1,0,0,1,10,20\" /></Border.RenderTransform></Border>", ".Matrix = new global::System.Numerics.Matrix3x2(1f, 0f, 0f, 1f, 10f, 20f);")]
	[Arguments("<TextBlock Foreground=\"Gold\" />", ".Foreground = new global::Vysara.Media.SolidColorBrush(global::Vysara.HdrColor.FromRgb(255, 215, 0));")]
	[Arguments("<Border><Border.Background><SolidColorBrush Color=\"Gold\" Opacity=\"0.5\" /></Border.Background></Border>", ".Color = global::Vysara.HdrColor.FromRgb(255, 215, 0);")]
	[Arguments("<Border Width=\"5.\" />", ".Width = 5f;")]
	[Arguments("<Border HorizontalAlignment=\"Left\" />", ".HorizontalAlignment = global::Vysara.HorizontalAlignment.Left;")]
	public void Values_convert_for_their_type(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<vysara:Badge Anchor=\"10\" />", "two numbers")]
	[Arguments("<vysara:Badge Anchor=\"1,2,3\" />", "two numbers")]
	[Arguments("<vysara:Badge Scale=\"a,b\" />", "two numbers")]
	[Arguments("<vysara:Badge Offset=\"1,2\" />", "three numbers")]
	[Arguments("<vysara:Badge Anchor=\"1e40,1\" />", "two numbers")]
	[Arguments("<Border Background=\"Reddish\" />", "named colour")]
	[Arguments("<Border Padding=\"1,2,3\" />", "one, two or four numbers")]
	[Arguments("<Border CornerRadius=\"1,2\" />", "one or four numbers")]
	[Arguments("<Border><Border.RenderTransform><MatrixTransform Matrix=\"1,0,0,1\" /></Border.RenderTransform></Border>", "six numbers")]
	[Arguments("<Border Background=\"Brushy\" />", "for a SolidColorBrush")]
	[Arguments("<Border Width=\"NaN\" />", "a number")]
	[Arguments("<Border Width=\"1e40\" />", "a number")]
	[Arguments("<Border HorizontalAlignment=\"Leftish\" />", "a member of HorizontalAlignment")]
	public void A_bad_value_is_VYSARA012_naming_the_syntax(string body, string syntax)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("VYSARA012");
		diagnostic.GetMessage().ShouldContain(syntax);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- {x:Null}, {x:Static} ---

	[Test]
	[Arguments("<vysara:Badge Halo=\"{x:Null}\" />", ".Halo = null;")]
	[Arguments("<TextBlock Text=\"{x:Null}\" />", ".Text = null;")]
	[Arguments("<TextBlock Text=\"{x:Static local:Palette.Title}\" />", ".Text = global::Demo.Palette.Title;")]
	[Arguments("<Border Background=\"{x:Static local:Palette.Accent}\" />", ".Background = global::Demo.Palette.Accent;")]
	[Arguments("<Border Width=\"{x:Static local:Palette.Gap}\" />", ".Width = global::Demo.Palette.Gap;")]
	[Arguments("<Border HorizontalAlignment=\"{x:Static HorizontalAlignment.Right}\" />", ".HorizontalAlignment = global::Vysara.HorizontalAlignment.Right;")]
	[Arguments("<Border Background=\"{x:Static Demo.Palette.Accent}\" />", ".Background = global::Demo.Palette.Accent;")]
	[Arguments("<Canvas><Border x:Name=\"S\" Canvas.Left=\"{x:Static local:Palette.Gap}\" /></Canvas>", "global::Vysara.Controls.Canvas.SetLeft(S, global::Demo.Palette.Gap);")]
	public void Value_markup_resolves_at_compile_time(string body, string expected) =>
		Generates(body).ShouldContain(expected);

	[Test]
	[Arguments("<Border Width=\"{x:Null}\" />", "VYSARA012")]
	[Arguments("<Border Width=\"{x:Static local:Palette.Nope}\" />", "VYSARA017")]
	[Arguments("<Border Width=\"{x:Static Nope.Member}\" />", "VYSARA017")]
	[Arguments("<Border Width=\"{x:Static local:Palette.Title}\" />", "VYSARA012")]
	[Arguments("<Border Background=\"{x:Static local:Palette.Ink}\" />", "VYSARA012")] // a colour is not a brush
	public void Bad_value_markup_is_an_error_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Attached properties ---

	[Test]
	[Arguments("<Grid><Border Grid.Row=\"x\" /></Grid>", "VYSARA012")]
	[Arguments("<Grid><Border Grid.Bogus=\"1\" /></Grid>", "VYSARA004")]
	[Arguments("<Border Nope.Row=\"1\" />", "VYSARA003")]
	[Arguments("<Border vysara:Glow.Radius=\"wide\" />", "VYSARA012")]
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
			      <Setter Property="vysara:Glow.Radius"><Setter.Value>2</Setter.Value></Setter>
			    </Style>
			  </ResourceDictionary>
			</StackPanel.Resources>
			<Border><TextBlock x:Name="T" Style="{StaticResource Derived}" /></Border>
			""");
		code.ShouldContain("Root.Resources.MergedDictionaries.Clear();");
		code.ShouldContain("Root.Resources.MergedDictionaries.Add(__merged");
		code.ShouldContain("Property = global::Vysara.Controls.Canvas.LeftProperty, Value = 3f");
		code.ShouldContain("Property = global::Vysara.Extensions.Glow.RadiusProperty, Value = 2f");
		// The lookup reaches the markup ancestor that declared the resources: nothing is parented yet.
		code.ShouldContain("T.Style = (T.FindResource(\"Derived\") ?? Root.FindResource(\"Derived\")) as global::Vysara.Style;");
	}

	[Test]
	[Arguments("<StackPanel.Resources><ResourceDictionary Source=\"Theme.xaml\" /></StackPanel.Resources>", "VYSARA020")]
	[Arguments("<StackPanel.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><Style x:Key=\"S\" TargetType=\"Border\" /></ResourceDictionary.MergedDictionaries></ResourceDictionary></StackPanel.Resources>", "VYSARA020")]
	[Arguments("<StackPanel.Resources><Style x:Key=\"S\" TargetType=\"Border\"><Setter Property=\"Canvas.Bogus\" Value=\"1\" /></Style></StackPanel.Resources>", "VYSARA018")]
	[Arguments("<StackPanel.Resources><Style x:Key=\"S\" TargetType=\"Border\" BasedOn=\"Base\" /></StackPanel.Resources>", "VYSARA007")]
	public void A_bad_resource_form_is_an_error(string body, string id) =>
		Fails(body).Id.ShouldBe(id);

	// --- Directives ---

	[Test]
	public void X_FieldModifier_sets_the_named_elements_field_as_WinUI_reads_it()
	{
		var code = Generates("""
			<TextBlock x:Name="A" x:FieldModifier="Public" />
			<TextBlock x:Name="B" x:FieldModifier="private" Text="{x:Bind Caption, Mode=OneWay}" />
			<TextBlock x:Name="C" x:FieldModifier="Friend" />
			<TextBlock x:Name="D" x:FieldModifier="protected" />
			<TextBlock x:Name="E" />
			""");
		code.ShouldContain("public Vysara.Controls.TextBlock A = null!;");
		code.ShouldContain("private Vysara.Controls.TextBlock B = null!;");
		code.ShouldContain("internal Vysara.Controls.TextBlock C = null!;");
		code.ShouldContain("protected Vysara.Controls.TextBlock D = null!;");
		code.ShouldContain("internal Vysara.Controls.TextBlock E = null!;");
	}

	[Test]
	public void An_x_FieldModifier_WinUI_does_not_take_is_VYSARA017_at_its_line()
	{
		var diagnostic = Fails("""<TextBlock x:Name="A" x:FieldModifier="sealed" />""");
		diagnostic.Id.ShouldBe("VYSARA017");
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	public void X_Uid_is_ignored_with_a_warning_and_the_text_shows()
	{
		var xaml = View("""<TextBlock x:Uid="Greeting" Text="hi" />""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("VYSARA013");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Warning);
		diagnostic.GetMessage().ShouldContain("x:Uid 'Greeting'");
		Line(diagnostic).ShouldBe(BodyLine);
		result.AllGeneratedText().ShouldContain(".Text = \"hi\";");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate x:DataType=\"local:Caption\" x:DefaultBindMode=\"OneWay\"><TextBlock /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>", "VYSARA025")]
	[Arguments("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate x:Phase=\"1\"><TextBlock /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>", "VYSARA025")]
	[Arguments("<ContentControl><ContentControl.Template><ControlTemplate x:DataType=\"local:Caption\"><Border /></ControlTemplate></ContentControl.Template></ContentControl>", "VYSARA025")]
	[Arguments("<ContentControl><ContentControl.Template><ControlTemplate Width=\"4\"><Border /></ControlTemplate></ContentControl.Template></ContentControl>", "VYSARA025")]
	[Arguments("<ContentControl><ContentControl.Template><ControlTemplate><ControlTemplate.Resources /><Border /></ControlTemplate></ContentControl.Template></ContentControl>", "VYSARA020")]
	[Arguments("<StackPanel.Resources><ControlTemplate x:Key=\"T\" x:DefaultBindMode=\"OneWay\"><Border /></ControlTemplate></StackPanel.Resources>", "VYSARA025")]
	public void What_a_template_element_carries_and_does_not_build_is_an_error_at_its_line(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Construction and setters ---

	[Test]
	public void A_struct_and_a_constructor_the_generated_class_reaches_are_built()
	{
		var code = Generates("""<StackPanel.Resources><local:Pair x:Key="P" A="1" /></StackPanel.Resources><local:Hidden />""");
		code.ShouldContain("new Demo.Pair();");
		code.ShouldContain(".A = 1f;");
		code.ShouldContain("new Demo.Hidden();");
	}

	[Test]
	public void A_named_struct_is_kept_in_a_field_that_starts_at_its_default()
	{
		var xaml = View("""<StackPanel.Resources><local:Pair x:Name="Named" x:Key="P" A="2" /></StackPanel.Resources>""");
		XamlGeneratorHarness.Run(xaml, CodeBehind).AllGeneratedText().ShouldContain("internal Demo.Pair Named = default;");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	public void An_element_whose_constructor_the_generated_class_cannot_reach_is_VYSARA025() =>
		Fails("<local:Closed />").Id.ShouldBe("VYSARA025");

	[Test]
	public void A_setter_the_generated_class_reaches_is_set_as_an_attribute_and_as_a_property_element()
	{
		var code = Generates("""<local:Gate Inside="1" /><local:Gate><local:Gate.Inside>2</local:Gate.Inside></local:Gate>""");
		code.ShouldContain(".Inside = 1f;");
		code.ShouldContain(".Inside = 2f;");
	}

	[Test]
	[Arguments("<local:Gate Shut=\"1\" />", "private setter")]
	[Arguments("<local:Gate><local:Gate.Shut>1</local:Gate.Shut></local:Gate>", "private setter")]
	[Arguments("<local:Gate Built=\"1\" />", "init-only")]
	[Arguments("<local:Gate><local:Gate.Built>1</local:Gate.Built></local:Gate>", "init-only")]
	[Arguments("<local:Level Depth=\"1\" />", "protected setter")]
	public void A_setter_the_generated_class_cannot_call_is_VYSARA025_at_its_line(string body, string reason)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("VYSARA025");
		diagnostic.GetMessage().ShouldContain(reason);
		Line(diagnostic).ShouldBe(BodyLine);
	}

	[Test]
	public void A_protected_setter_is_set_on_an_unsealed_root_which_the_generated_class_derives_from()
	{
		const string xaml = """
			<local:Level xmlns="https://github.com/AtomicBlom/Vysara/ui"
			             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			             xmlns:local="using:Demo"
			             x:Class="Demo.W" Depth="2">
			  <local:Level.Depth>3</local:Level.Depth>
			</local:Level>
			""";
		var codeBehind = CodeBehind.Replace("public partial class V", "public partial class W { } public partial class V");
		var result = XamlGeneratorHarness.Run(xaml, codeBehind);
		result.Diagnostics.ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain("this.Depth = 2f;");
		XamlGeneratorHarness.CompileErrors(xaml, codeBehind).ShouldBeEmpty();
	}

	[Test]
	public void An_internal_setter_is_set_where_the_library_gives_the_view_access_to_its_internals()
	{
		var library = XamlGeneratorHarness.Library("Lib", $$"""
			[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("{{XamlGeneratorHarness.AssemblyName}}")]
			namespace Lib { public sealed class Dial : Vysara.Controls.ContentControl { public float Turn { get; internal set; } } }
			""");
		var xaml = View("""<lib:Dial xmlns:lib="using:Lib" Turn="3" />""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind, libraries: [library]);
		result.Diagnostics.ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain(".Turn = 3f;");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind, libraries: [library]).ShouldBeEmpty();
	}

	// --- Ignorable markup and extra children in the forms that take one ---

	[Test]
	public void Design_time_markup_beside_a_template_a_panel_or_a_storyboard_is_skipped()
	{
		var code = Generates("""
			<ItemsControl>
			  <ItemsControl.ItemsPanel><d:ItemsPanelTemplate /><ItemsPanelTemplate><d:Border /><VirtualizingStackPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
			  <ItemsControl.ItemTemplate><d:DataTemplate /><DataTemplate><TextBlock /></DataTemplate></ItemsControl.ItemTemplate>
			</ItemsControl>
			<Border x:Name="Fade" />
			<ContentControl>
			  <VisualStateManager.VisualStateGroups>
			    <VisualStateGroup x:Name="G">
			      <VisualState x:Name="A">
			        <VisualState.Storyboard><d:Storyboard /><Storyboard><DoubleAnimation Storyboard.TargetName="Fade" Storyboard.TargetProperty="Opacity" To="0" /></Storyboard></VisualState.Storyboard>
			      </VisualState>
			    </VisualStateGroup>
			  </VisualStateManager.VisualStateGroups>
			</ContentControl>
			""");
		code.ShouldContain("new Vysara.Controls.VirtualizingStackPanel()");
		code.ShouldContain("new global::Vysara.Animation.DoubleAnimation");
	}

	[Test]
	[Arguments("<ItemsControl><ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel /></ItemsPanelTemplate><ItemsPanelTemplate><StackPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>")]
	[Arguments("<ItemsControl><ItemsControl.ItemsPanel><Border /><ItemsPanelTemplate><StackPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>")]
	[Arguments("<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><TextBlock /></DataTemplate><DataTemplate><TextBlock /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>")]
	[Arguments("<ContentControl><ContentControl.Template><ControlTemplate><Border /></ControlTemplate><Border /></ContentControl.Template></ContentControl>")]
	[Arguments("<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name=\"G\"><VisualState x:Name=\"A\"><VisualState.Storyboard><Storyboard /><Storyboard /></VisualState.Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>")]
	[Arguments("<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name=\"G\"><VisualState x:Name=\"A\"><VisualState.Storyboard /></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>")]
	public void A_second_child_where_one_is_taken_is_VYSARA020_at_its_line(string body)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe("VYSARA020");
		Line(diagnostic).ShouldBe(BodyLine);
	}

	// --- Grid definitions ---

	[Test]
	public void A_definitions_size_is_read_from_a_property_element()
	{
		var code = Generates("""<Grid><Grid.RowDefinitions><RowDefinition><RowDefinition.Height>Auto</RowDefinition.Height></RowDefinition></Grid.RowDefinitions><Grid.ColumnDefinitions><ColumnDefinition><ColumnDefinition.Width>2*</ColumnDefinition.Width></ColumnDefinition></Grid.ColumnDefinitions></Grid>""");
		code.ShouldContain(".RowDefinitions.Add(global::Vysara.GridLength.Auto);");
		code.ShouldContain(".ColumnDefinitions.Add(global::Vysara.GridLength.Stars(2f));");
	}

	[Test]
	[Arguments("<Grid><Grid.RowDefinitions><RowDefinition><RowDefinition.MinHeight>40</RowDefinition.MinHeight></RowDefinition></Grid.RowDefinitions></Grid>", "VYSARA025")]
	[Arguments("<Grid><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"><ColumnDefinition.Width>Auto</ColumnDefinition.Width></ColumnDefinition></Grid.ColumnDefinitions></Grid>", "VYSARA025")]
	[Arguments("<Grid><Grid.RowDefinitions><RowDefinition><RowDefinition.Height><Border /></RowDefinition.Height></RowDefinition></Grid.RowDefinitions></Grid>", "VYSARA020")]
	[Arguments("<Grid><Grid.RowDefinitions><RowDefinition><RowDefinition.Height>tall</RowDefinition.Height></RowDefinition></Grid.RowDefinitions></Grid>", "VYSARA012")]
	public void A_definition_property_element_other_than_its_size_is_reported_not_dropped(string body, string id)
	{
		var diagnostic = Fails(body);
		diagnostic.Id.ShouldBe(id);
		Line(diagnostic).ShouldBe(BodyLine);
	}
}
