using System.Text.RegularExpressions;

using SUIsei.Controls;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// The generator's source table: generated code records each element's and each style setter's XAML file, line and
/// column through <see cref="Diagnostics.RecordSource(UIElement, string, int, int, bool)"/>, in one block behind
/// <see cref="Diagnostics.Enabled"/>, with a <c>ControlTemplate</c>'s elements marked as template elements. Asserted on
/// the generated text and a compile, then end to end on the committed sample views, which this project builds with
/// diagnostics on.
/// </summary>
public sealed partial class SourceTableTests
{
	private const string Vm = """
		namespace Demo
		{
			public partial class V { }
			public class Widget { public string Title { get; set; } = ""; }
		}
		""";

	// Line and column are where each element's tag name starts; the column counts from 1.
	private const string PageXaml = """
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            x:Class="Demo.V">
		  <TextBlock x:Name="Title" Text="Hi" />
		  <Border>
		    <TextBlock />
		  </Border>
		</StackPanel>
		""";

	[Test]
	public void Initialize_component_records_each_element_behind_the_switch()
	{
		var result = XamlGeneratorHarness.Run(PageXaml, Vm, "Views/Main.xaml");
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("if (global::SUIsei.Diagnostics.Enabled)");
		code.ShouldContain("global::SUIsei.Diagnostics.RecordSource(Root, \"Views/Main.xaml\", 1, 2, false);");
		code.ShouldContain("global::SUIsei.Diagnostics.RecordSource(Title, \"Views/Main.xaml\", 4, 4, false);");
		RecordedPositions(code).ShouldBe(["1, 2", "4, 4", "5, 4", "6, 6"]);
		XamlGeneratorHarness.CompileErrors(PageXaml, Vm, "Views/Main.xaml").ShouldBeEmpty();
	}

	[Test]
	public void The_records_come_after_the_tree_is_built_in_one_block()
	{
		var code = XamlGeneratorHarness.Run(PageXaml, Vm).AllGeneratedText();

		var guard = code.IndexOf("if (global::SUIsei.Diagnostics.Enabled)", StringComparison.Ordinal);
		guard.ShouldBeGreaterThan(code.LastIndexOf("Root.Children.Add(", StringComparison.Ordinal));
		code.IndexOf("if (global::SUIsei.Diagnostics.Enabled)", guard + 1, StringComparison.Ordinal).ShouldBe(-1);
	}

	[Test]
	public void A_control_templates_elements_are_marked_as_template_elements_and_a_data_templates_are_not()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <ContentControl>
			    <ContentControl.Template>
			      <ControlTemplate TargetType="ContentControl">
			        <Border x:Name="Chrome" />
			      </ControlTemplate>
			    </ContentControl.Template>
			  </ContentControl>
			  <ItemsControl>
			    <ItemsControl.ItemTemplate>
			      <DataTemplate x:DataType="Demo.Widget">
			        <TextBlock x:Name="Caption" Text="{x:Bind Title}" />
			      </DataTemplate>
			    </ItemsControl.ItemTemplate>
			  </ItemsControl>
			</StackPanel>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("global::SUIsei.Diagnostics.RecordSource(Chrome, \"Test.xaml\", 7, 10, true);");
		code.ShouldContain("global::SUIsei.Diagnostics.RecordSource(Caption, \"Test.xaml\", 14, 10, false);");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Each_style_setter_is_recorded_by_its_place_in_the_style()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <StackPanel.Resources>
			    <Style x:Key="Heading" TargetType="TextBlock">
			      <Setter Property="FontSize" Value="24" />
			      <Setter Property="Opacity" Value="0.5" />
			    </Style>
			  </StackPanel.Resources>
			</StackPanel>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldMatch(@"RecordSource\(__style\d+\.Setters\[0\], ""Test\.xaml"", 6, 8\);");
		code.ShouldMatch(@"RecordSource\(__style\d+\.Setters\[1\], ""Test\.xaml"", 7, 8\);");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
	}

	// The "line, column" of each RecordSource call for an element, in emitted order.
	private static string[] RecordedPositions(string code) =>
		[.. ElementRecord().Matches(code).Select(m => m.Groups[1].Value)];

	[GeneratedRegex(@"RecordSource\([^,]+, ""[^""]*"", (\d+, \d+), (?:true|false)\);")]
	private static partial Regex ElementRecord();

	// --- End to end, on the committed sample views ---

	[Test]
	public void A_built_view_knows_where_each_element_was_written()
	{
		var view = new TemplatedView();
		view.InitializeComponent();
		view.List.ItemsSource = new[] { new Row { Label = "Alpha" } };

		var card = Diagnostics.SourceOf(view.Card).ShouldNotBeNull();
		Path.GetFileName(card.File).ShouldBe("TemplatedView.xaml");
		Path.IsPathRooted(card.File).ShouldBeTrue();
		(card.Line, card.Column, card.InTemplate).ShouldBe((13, 3, false));

		var chrome = Diagnostics.SourceOf(view.Card.GetTemplateRoot()!).ShouldNotBeNull();
		(chrome.Line, chrome.Column, chrome.InTemplate).ShouldBe((16, 6, true));

		var item = Diagnostics.SourceOf(view.List.ItemsHost!.Children[0]).ShouldNotBeNull();
		(item.Line, item.Column, item.InTemplate).ShouldBe((26, 6, false));

		var presenter = ((Border)view.Card.GetTemplateRoot()!).Child!;
		Diagnostics.SourceOf(presenter).ShouldNotBeNull().Line.ShouldBe(18);
	}

	[Test]
	public void A_style_value_on_a_built_view_names_the_setter_that_set_it()
	{
		var view = new ThemedView();
		view.InitializeComponent();

		Diagnostics.SourceOf(view).ShouldNotBeNull().Line.ShouldBe(7); // the unsealed root is the view itself
		var props = Diagnostics.Properties(view.Heading);
		var fontSize = props.Single(p => ReferenceEquals(p.Property, TextBlock.FontSizeProperty));
		fontSize.Level.ShouldBe(Diagnostics.ValueLevel.Style);
		(fontSize.Source!.Line, fontSize.Source.Column).ShouldBe((15, 8)); // from the BasedOn style
		var opacity = props.Single(p => ReferenceEquals(p.Property, UIElement.OpacityProperty));
		(opacity.Source!.Line, opacity.Source.Column).ShouldBe((20, 6));
	}
}
