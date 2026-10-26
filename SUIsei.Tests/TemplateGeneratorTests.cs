using Microsoft.CodeAnalysis;

namespace SUIsei.Tests;

/// <summary>
/// Generator coverage for templates + styles: <c>&lt;ControlTemplate&gt;</c> /
/// <c>&lt;DataTemplate&gt;</c> emit a static factory whose elements are locals and whose bindings resolve against a
/// scoped data root (the templated control / the data item) — never the page's fields; <c>{TemplateBinding}</c> and
/// item-scoped <c>{x:Bind}</c> wire inside that factory; <c>&lt;X.Resources&gt;</c> + <c>&lt;Style&gt;</c> resolve a
/// <c>Setter</c>'s <c>Property</c> to its dependency-property field. Asserted on generated text + a real compile pass.
/// </summary>
public sealed class TemplateGeneratorTests
{
	// A view-model the template binds against (compiled into the harness compilation as code-behind).
	private const string Vm = """
		namespace Demo
		{
			public partial class V { }
			public class Widget { public string Title { get; set; } = ""; public int Count { get; set; } }
		}
		""";

	private const string ControlTemplateXaml = """
		<ContentControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		                x:Class="Demo.V">
		  <ContentControl.Template>
		    <ControlTemplate TargetType="ContentControl">
		      <Border CornerRadius="4">
		        <ContentPresenter Content="{TemplateBinding Content}" />
		      </Border>
		    </ControlTemplate>
		  </ContentControl.Template>
		</ContentControl>
		""";

	[Test]
	public void Control_template_emits_a_static_factory_and_assigns_the_template()
	{
		var result = XamlGeneratorHarness.Run(ControlTemplateXaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		// The generated factory name carries a counter (an implementation detail); assert the stable signature shape
		// and that the same name is wired into the ControlTemplate.
		code.ShouldContain("private static global::SUIsei.FrameworkElement __Template");
		code.ShouldContain("(global::SUIsei.Controls.Control __tc)");
		code.ShouldContain("new global::SUIsei.Controls.ControlTemplate { Factory = __Template");
	}

	[Test]
	public void Template_binding_content_wires_initial_value_and_the_changed_event()
	{
		var code = XamlGeneratorHarness.Run(ControlTemplateXaml, Vm).AllGeneratedText();

		// {TemplateBinding Content}: ContentControl.Content is a CLR property with a ContentChanged event, so the
		// presenter is seeded and then kept live through that event (not a dependency-property push).
		code.ShouldContain(".Content = ((global::SUIsei.Controls.ContentControl)__tc).Content;");
		code.ShouldContain("((global::SUIsei.Controls.ContentControl)__tc).ContentChanged += (_, _) =>");
	}

	[Test]
	public void Control_template_compiles_against_the_runtime()
	{
		XamlGeneratorHarness.CompileErrors(ControlTemplateXaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Visual_states_inside_a_control_template_target_a_template_local_element()
	{
		// A templated control's CommonStates group, with a storyboard targeting a template-local element (Bg). The
		// target must resolve against the template's own name scope — not the page's fields.
		const string xaml = """
			<ContentControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			                x:Class="Demo.V">
			  <ContentControl.Template>
			    <ControlTemplate TargetType="ContentControl">
			      <Border x:Name="Bg" CornerRadius="4" Background="#000000">
			        <VisualStateManager.VisualStateGroups>
			          <VisualStateGroup Name="CommonStates">
			            <VisualState Name="Normal" />
			            <VisualState Name="PointerOver">
			              <Storyboard>
			                <ColorAnimation Storyboard.TargetName="Bg" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)"
			                                To="#5060A0" Duration="0:0:0.12" />
			              </Storyboard>
			            </VisualState>
			          </VisualStateGroup>
			        </VisualStateManager.VisualStateGroups>
			      </Border>
			    </ControlTemplate>
			  </ContentControl.Template>
			</ContentControl>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".Target = Bg;");
		code.ShouldContain("global::SUIsei.Animation.VisualStateManager.SetVisualStateGroups(Bg,");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Template_binding_outside_a_control_template_diagnoses()
	{
		// {TemplateBinding} on a plain page element (no enclosing ControlTemplate) is invalid.
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <ContentPresenter Content="{TemplateBinding Content}" />
			</StackPanel>
			""";
		XamlGeneratorHarness.Run(xaml, Vm).DiagnosticIds().ShouldContain("SUISEI019");
	}

	private const string DataTemplateXaml = """
		<ItemsControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		              x:Class="Demo.V">
		  <ItemsControl.ItemTemplate>
		    <DataTemplate x:DataType="Demo.Widget">
		      <TextBlock Text="{x:Bind Title}" FontSize="14" />
		    </DataTemplate>
		  </ItemsControl.ItemTemplate>
		</ItemsControl>
		""";

	[Test]
	public void Data_template_emits_an_item_factory_with_a_typed_data_root()
	{
		var result = XamlGeneratorHarness.Run(DataTemplateXaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("private static global::SUIsei.FrameworkElement __Template");
		code.ShouldContain("(object? __item)");
		code.ShouldContain("var __data = (global::Demo.Widget)__item!;");
		code.ShouldContain("new global::SUIsei.Controls.DataTemplate { Factory = __Template");
	}

	[Test]
	public void Item_scoped_bind_reads_the_data_item_once()
	{
		var code = XamlGeneratorHarness.Run(DataTemplateXaml, Vm).AllGeneratedText();

		// {x:Bind Title} inside the DataTemplate resolves against __data (the item), not the page — a OneTime read.
		code.ShouldContain(".Text = (__data.Title) ?? string.Empty;");
		// The page's _Bindings tracker (INPC) is not generated for a template-scoped bind.
		code.ShouldNotContain("_Bindings");
	}

	[Test]
	public void Data_template_compiles_against_the_runtime()
	{
		XamlGeneratorHarness.CompileErrors(DataTemplateXaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Item_scoped_bind_without_a_data_type_diagnoses()
	{
		const string xaml = """
			<ItemsControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			              x:Class="Demo.V">
			  <ItemsControl.ItemTemplate>
			    <DataTemplate>
			      <TextBlock Text="{x:Bind Title}" />
			    </DataTemplate>
			  </ItemsControl.ItemTemplate>
			</ItemsControl>
			""";
		XamlGeneratorHarness.Run(xaml, Vm).DiagnosticIds().ShouldContain("SUISEI008");
	}

	[Test]
	public void Items_panel_emits_a_panel_factory_that_sets_the_panels_attributes()
	{
		const string xaml = """
			<ItemsControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			              xmlns:suisei="https://github.com/AtomicBlom/SUIsei/extensions"
			              x:Class="Demo.V">
			  <ItemsControl.ItemsPanel>
			    <ItemsPanelTemplate>
			      <VirtualizingStackPanel x:Name="Host" suisei:Virtualization.RowHeight="32" Margin="2" />
			    </ItemsPanelTemplate>
			  </ItemsControl.ItemsPanel>
			</ItemsControl>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".ItemsPanel = new global::SUIsei.Controls.ItemsPanelTemplate { Factory = __Template");
		code.ShouldContain("private static global::SUIsei.Controls.Panel __Template");
		code.ShouldContain("global::SUIsei.Extensions.Virtualization.SetRowHeight(Host, 32f);");
		code.ShouldContain("Host.Margin = new global::SUIsei.Thickness(2f);");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void An_items_panel_that_is_not_a_panel_is_an_error()
	{
		const string xaml = """
			<ItemsControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			              x:Class="Demo.V">
			  <ItemsControl.ItemsPanel>
			    <ItemsPanelTemplate>
			      <Border />
			    </ItemsPanelTemplate>
			  </ItemsControl.ItemsPanel>
			</ItemsControl>
			""";
		XamlGeneratorHarness.Run(xaml, Vm).DiagnosticIds().ShouldBe(["SUISEI020"]);
	}

	// --- Style + Resources markup ---

	private const string StyleXaml = """
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            x:Class="Demo.V">
		  <StackPanel.Resources>
		    <Style x:Key="Heading" TargetType="TextBlock">
		      <Setter Property="FontSize" Value="24" />
		    </Style>
		  </StackPanel.Resources>
		  <TextBlock x:Name="Title" Style="{StaticResource Heading}" />
		</StackPanel>
		""";

	[Test]
	public void Style_resource_resolves_setter_property_to_its_dependency_property_field()
	{
		var result = XamlGeneratorHarness.Run(StyleXaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".Resources ??= new global::SUIsei.ResourceDictionary();");
		code.ShouldContain("new global::SUIsei.Setter { Property = global::SUIsei.Controls.TextBlock.FontSizeProperty, Value = 24f }");
		code.ShouldContain(".Resources.Add(\"Heading\",");
	}

	[Test]
	public void Style_markup_compiles_against_the_runtime()
	{
		XamlGeneratorHarness.CompileErrors(StyleXaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Setter_for_an_unstylable_property_diagnoses()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <StackPanel.Resources>
			    <Style x:Key="Bad" TargetType="TextBlock">
			      <Setter Property="Nonsense" Value="1" />
			    </Style>
			  </StackPanel.Resources>
			</StackPanel>
			""";
		XamlGeneratorHarness.Run(xaml, Vm).DiagnosticIds().ShouldContain("SUISEI018");
	}
}
