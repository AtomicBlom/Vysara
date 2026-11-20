namespace SUIsei.Tests;

/// <summary>
/// Generator coverage for live <c>{x:Bind}</c>: what TwoWay writes back and when it can't (SUISEI028), function bindings
/// and the ways one fails to resolve (SUISEI029), a malformed <c>{x:Bind}</c> (SUISEI030), and the code a DataTemplate's
/// tracked bindings become. <c>LiveBindingTests</c> runs the same forms end to end.
/// </summary>
public sealed class XamlLiveBindingGeneratorTests
{
	private const string CodeBehind = """
		namespace Demo
		{
			public partial class V
			{
				public Vm ViewModel { get; } = new();
				public bool Busy { get; set; }
				public float Volume { get; set; }
				public int Count { get; }
				public System.Collections.Generic.List<Row> Rows { get; } = new();
			}
			public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
			{
				public string? Caption { get; set; }
				public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
			}
			public sealed class Row : System.ComponentModel.INotifyPropertyChanged
			{
				public bool On { get; set; }
				public float Level { get; set; }
				public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
			}
			public static class Fmt
			{
				public static string Percent(float value) => value.ToString();
				public static string Twice(int value) => value.ToString();
				public static string Twice(long value) => value.ToString();
				public static string Pick(float a, double b) => "";
				public static string Pick(double a, float b) => "";
			}
			public sealed class Inst
			{
				public string Instance(float value) => "";
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

	private static string Row(string body) => View($$"""
		<ItemsControl ItemsSource="{x:Bind Rows}"><ItemsControl.ItemTemplate><DataTemplate x:DataType="local:Row">{{body}}</DataTemplate></ItemsControl.ItemTemplate></ItemsControl>
		""");

	[Test]
	[Arguments("""<ToggleButton IsChecked="{x:Bind Busy, Mode=TwoWay}" />""", "(_root.__b")]
	[Arguments("""<Slider Value="{x:Bind Volume, Mode=TwoWay}" />""", "_root.Volume = (float)(")]
	public void TwoWay_converts_the_controls_value_for_its_source(string body, string writeBack)
	{
		var xaml = View(body);
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);

		result.DiagnosticIds().ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain(writeBack);
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("""<Button ToolTipService.ToolTip="{x:Bind ViewModel.Caption, Mode=OneWay}" />""", "global::SUIsei.Controls.ToolTipService.SetToolTip(_root.")]
	[Arguments("""<Button ToolTipService.ToolTip="{x:Bind local:Fmt.Percent(Volume)}" />""", "global::SUIsei.Controls.ToolTipService.SetToolTip(")]
	public void An_attached_property_binds_through_its_owners_setter(string body, string set)
	{
		var xaml = View(body);
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);

		result.DiagnosticIds().ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain(set);
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	public void An_attached_property_binds_in_a_DataTemplate()
	{
		var xaml = Row("""<Border ToolTipService.ToolTip="{x:Bind Level, Mode=OneWay}" />""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);

		result.DiagnosticIds().ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain("global::SUIsei.Controls.ToolTipService.SetToolTip(_e_");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	/// <summary>An attached property has no change event, so a TwoWay binding on it can't write back: SUISEI028, as for any
	/// binding that can't, at its XAML line.</summary>
	[Test]
	public void TwoWay_on_an_attached_property_cannot_write_back()
	{
		var xaml = View("""
			<Button
			    ToolTipService.ToolTip="{x:Bind ViewModel.Caption, Mode=TwoWay}" />
			""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);

		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("SUISEI028");
		diagnostic.GetMessage().ShouldContain("ToolTip");
		var line = Array.FindIndex(xaml.Split('\n'), l => l.Contains("ToolTipService.ToolTip"));
		diagnostic.Location.GetLineSpan().StartLinePosition.Line.ShouldBe(line);
	}

	/// <summary>A ControlTemplate's {x:Bind} gives its part a value as the template's other values do, on the part's Template
	/// rung, an attached property's and a plain one's alike, so a value the app sets on the part wins.</summary>
	[Test]
	[Arguments("""<Border ToolTipService.ToolTip="{x:Bind Tag}" />""", "global::SUIsei.Controls.ToolTipService.ToolTipProperty")]
	[Arguments("""<Border Opacity="{x:Bind Opacity}" />""", "global::SUIsei.UIElement.OpacityProperty")]
	public void A_ControlTemplates_xBind_goes_on_the_parts_Template_rung(string part, string dp)
	{
		var xaml = View($$"""<ContentControl><ContentControl.Template><ControlTemplate TargetType="ContentControl">{{part}}</ControlTemplate></ContentControl.Template></ContentControl>""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);

		result.DiagnosticIds().ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain($"global::SUIsei.Generated.TemplateValues.Set(");
		result.AllGeneratedText().ShouldContain(dp);
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	public void TwoWay_reads_the_source_again_after_writing_it()
	{
		var text = XamlGeneratorHarness.Run(View("""<ToggleButton IsChecked="{x:Bind Busy, Mode=TwoWay}" />"""), CodeBehind).AllGeneratedText();

		// The reverse wire ends by pushing the source's value, outside its guard, so a refused value is shown as it is.
		System.Text.RegularExpressions.Regex.IsMatch(text, @"finally \{ _updating_0 = false; \}\s*Update_0\(\);").ShouldBeTrue();
	}

	[Test]
	[Arguments("""<Slider Value="{x:Bind Count, Mode=TwoWay}" />""")]                                   // no setter
	[Arguments("""<TextBlock Text="{x:Bind local:Fmt.Percent(Volume), Mode=TwoWay}" />""")]          // a function
	[Arguments("""<ToggleButton Visibility="{x:Bind Busy, Mode=TwoWay}" />""")]                       // Visibility back to bool
	public void TwoWay_that_cannot_write_back_is_SUISEI028(string body) =>
		XamlGeneratorHarness.Run(View(body), CodeBehind).DiagnosticIds().ShouldBe(["SUISEI028"]);

	[Test]
	[Arguments("local:Nope.Percent(Volume)")]   // no such type
	[Arguments("local:Fmt.Nope(Volume)")]       // no such method
	[Arguments("local:Inst.Instance(Volume)")]   // not static
	[Arguments("local:Fmt.Percent(Busy)")]      // no overload takes a bool
	[Arguments("local:Fmt.Pick(Volume, Volume)")] // two overloads take it, neither exactly
	public void A_function_that_does_not_resolve_is_SUISEI029(string function) =>
		XamlGeneratorHarness.Run(View($$"""<TextBlock Text="{x:Bind {{function}}}" />"""), CodeBehind).DiagnosticIds().ShouldBe(["SUISEI029"]);

	[Test]
	public void An_exact_overload_wins_over_one_that_converts()
	{
		var xaml = View("""<TextBlock Text="{x:Bind local:Fmt.Twice(Count)}" />""");

		XamlGeneratorHarness.Run(xaml, CodeBehind).DiagnosticIds().ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("""<TextBlock Text="{x:Bind}" />""")]                         // no path on a page
	[Arguments("""<TextBlock Text="{x:Bind Fmt.Percent(Volume)}" />""")]     // a function without its xmlns prefix
	[Arguments("""<TextBlock Text="{x:Bind local:Fmt.Percent(Volume}" />""")] // unbalanced
	[Arguments("""<TextBlock Text="{x:Bind Busy, Mode=Sometimes}" />""")]   // no such mode
	public void A_malformed_x_Bind_is_SUISEI030(string body) =>
		XamlGeneratorHarness.Run(View(body), CodeBehind).DiagnosticIds().ShouldBe(["SUISEI030"]);

	[Test]
	public void A_function_result_the_property_cannot_take_is_SUISEI009() =>
		XamlGeneratorHarness.Run(View("""<Slider Value="{x:Bind local:Fmt.Percent(Volume)}" />"""), CodeBehind).DiagnosticIds().ShouldBe(["SUISEI009"]);

	[Test]
	public void A_DataTemplates_tracked_bindings_get_a_helper_kept_with_the_element_it_builds()
	{
		var xaml = Row("""<StackPanel><ToggleButton IsChecked="{x:Bind On, Mode=TwoWay}" /><TextBlock Text="{x:Bind local:Fmt.Percent(Level), Mode=OneWay}" Visibility="{x:Bind On, Mode=OneWay}" /></StackPanel>""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var text = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		text.ShouldContain("_Bindings : global::SUIsei.Controls.IDataTemplateBindings");
		text.ShouldContain("global::SUIsei.Controls.DataTemplate.SetBindings(");
		text.ShouldContain("? global::SUIsei.Visibility.Visible : global::SUIsei.Visibility.Collapsed");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	public void A_DataTemplate_with_only_OneTime_bindings_gets_no_helper()
	{
		var text = XamlGeneratorHarness.Run(Row("""<TextBlock Text="{x:Bind local:Fmt.Percent(Level)}" />"""), CodeBehind).AllGeneratedText();

		text.ShouldNotContain("IDataTemplateBindings");
		System.Text.RegularExpressions.Regex.IsMatch(text, @"__t\d+\.Text = \(global::Demo\.Fmt\.Percent\(__data\.Level\)\) \?\? string\.Empty;").ShouldBeTrue();
	}

	[Test]
	public void A_tracked_template_binding_needs_the_items_type() =>
		XamlGeneratorHarness.Run(View("""<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{x:Bind Level, Mode=OneWay}" /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>"""), CodeBehind)
			.DiagnosticIds().ShouldBe(["SUISEI008"]);

	[Test]
	public void An_untyped_templates_item_itself_binds_as_an_object()
	{
		var xaml = View("""<ItemsControl><ItemsControl.ItemTemplate><DataTemplate><ContentControl Content="{x:Bind}" /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>""");

		XamlGeneratorHarness.Run(xaml, CodeBehind).DiagnosticIds().ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}
}
