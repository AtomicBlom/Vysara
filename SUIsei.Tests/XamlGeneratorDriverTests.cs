using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SUIsei.Tests;

/// <summary>
/// Generator correctness tests driven through <see cref="XamlGeneratorHarness"/> (a real
/// <see cref="Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver"/>): invalid-input diagnostics and incremental
/// caching that the build-time analyzer path can't reach. Happy-path generation is covered by
/// <see cref="XamlGeneratorTests"/> / <see cref="XamlBindingTests"/> against committed views.
/// </summary>
public sealed class XamlGeneratorDriverTests
{
	[Test]
	public void Valid_xaml_generates_a_partial_with_named_fields_and_no_diagnostics()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
						xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
						x:Class="Demo.MyView" Orientation="Vertical">
				<TextBlock x:Name="Title" Text="Hi" FontSize="24" />
			</StackPanel>
			""";

		var result = XamlGeneratorHarness.Run(xaml, "namespace Demo { internal partial class MyView {} }");

		result.Diagnostics.ShouldBeEmpty();
		var gen = result.AllGeneratedText();
		gen.ShouldContain("partial class MyView");
		gen.ShouldContain("Title");
	}

	[Test]
	public void Output_is_cached_when_an_unrelated_compilation_change_leaves_it_identical()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
						xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
						x:Class="Demo.MyView" Orientation="Vertical">
				<TextBlock x:Name="Title" Text="Hi" />
			</StackPanel>
			""";
		var (driver, compilation) = XamlGeneratorHarness.Create(xaml, "namespace Demo { internal partial class MyView {} }");

		driver = driver.RunGenerators(compilation);
		// An edit elsewhere in the compilation that does not change the view's generated output.
		var changed = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Other { class Unrelated { } }"));
		driver = driver.RunGenerators(changed);

		// The value-equatable result short-circuits the re-emit: the tracked step recomputes (the compilation
		// changed) but its equal output is reported Unchanged, never Modified/New — i.e. nothing is re-emitted.
		var steps = driver.GetRunResult().Results[0].TrackedSteps["XamlGenerate"];
		var reasons = steps.SelectMany(s => s.Outputs).Select(o => o.Reason).ToArray();
		reasons.ShouldNotBeEmpty();
		reasons.ShouldAllBe(r => r == IncrementalStepRunReason.Unchanged || r == IncrementalStepRunReason.Cached);
	}

	[Test]
	public void Same_short_class_name_in_different_namespaces_does_not_collide()
	{
		static string View(string fqClass) => $"""
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
						xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
						x:Class="{fqClass}" Orientation="Vertical" />
			""";

		// Two views whose short class name is identical (A.MainView vs B.MainView): a short-name hint
		// ("MainView.g.cs") would be emitted twice and crash the compilation; the fully-qualified hint must not.
		var result = XamlGeneratorHarness.RunMany(
			[("A.xaml", View("A.MainView")), ("B.xaml", View("B.MainView"))],
			"namespace A { internal partial class MainView { } } namespace B { internal partial class MainView { } }");

		result.Diagnostics.ShouldBeEmpty();
		var hints = result.HintNames();
		hints.Length.ShouldBe(2);
		hints.Distinct().Count().ShouldBe(2);
	}

	// A valid SUIsei view (StackPanel root) wrapping the given body / extra root attributes.
	private static string Wrap(string body, string rootAttrs = "") => $"""
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
					xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
					x:Class="Demo.V" {rootAttrs}>
			{body}
		</StackPanel>
		""";

	[Test]
	public void Invalid_x_Name_is_diagnosed_and_not_emitted_as_a_broken_identifier()
	{
		// 'event' is a C# keyword; emitting it verbatim as a field name would be uncompilable.
		var result = XamlGeneratorHarness.Run(Wrap("""<TextBlock x:Name="event" Text="hi" />"""));

		result.DiagnosticIds().ShouldContain("SUISEI011");
		var gen = result.AllGeneratedText();
		gen.ShouldContain("partial class V");   // the view still generates (compilable), just without the bad field
		gen.ShouldNotContain("TextBlock event");
	}

	[Test]
	public void Duplicate_x_Name_is_diagnosed()
	{
		var result = XamlGeneratorHarness.Run(Wrap("""
			<TextBlock x:Name="Dup" />
			<TextBlock x:Name="Dup" />
			"""));
		result.DiagnosticIds().ShouldContain("SUISEI011");
	}

	[Test]
	public void x_Name_colliding_with_the_reserved_Root_field_is_diagnosed()
	{
		XamlGeneratorHarness.Run(Wrap("""<TextBlock x:Name="Root" />"""))
			.DiagnosticIds().ShouldContain("SUISEI011");
	}

	[Test]
	public void Invalid_enum_member_is_diagnosed()
	{
		XamlGeneratorHarness.Run(Wrap("", rootAttrs: """Orientation="Sideways" """))
			.DiagnosticIds().ShouldContain("SUISEI012");
	}

	[Test]
	public void Non_numeric_value_for_a_numeric_property_is_diagnosed()
	{
		XamlGeneratorHarness.Run(Wrap("""<TextBlock FontSize="big" />"""))
			.DiagnosticIds().ShouldContain("SUISEI012");
	}

	[Test]
	public void Element_in_a_foreign_namespace_is_reported_unknown()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
						xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
						xmlns:f="urn:foreign" x:Class="Demo.V">
				<f:Widget />
			</StackPanel>
			""";
		XamlGeneratorHarness.Run(xaml).DiagnosticIds().ShouldContain("SUISEI003");
	}

	[Test]
	public void Unsupported_property_element_is_surfaced_not_silently_dropped()
	{
		// Recognized property elements (Resources/Template/VisualStateGroups/ItemTemplate/ItemsPanel) are handled;
		// any other is surfaced as a warning rather than silently dropped.
		XamlGeneratorHarness.Run(Wrap("<StackPanel.Margin />"))
			.DiagnosticIds().ShouldContain("SUISEI020");
	}

	[Test]
	public void Inline_text_content_is_surfaced_not_silently_dropped()
	{
		XamlGeneratorHarness.Run(Wrap("<TextBlock>Hello</TextBlock>"))
			.DiagnosticIds().ShouldContain("SUISEI013");
	}
}
