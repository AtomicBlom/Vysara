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
	public void Template_binding_content_wires_the_control_s_content_template_binding()
	{
		var code = XamlGeneratorHarness.Run(ControlTemplateXaml, Vm).AllGeneratedText();

		// {TemplateBinding Content}: ContentControl.Content is a CLR property, so the presenter is wired through the
		// control's content template binding, which seeds it and keeps it live until the template is replaced.
		code.ShouldContain("((global::SUIsei.Controls.ContentControl)__tc).RegisterContentTemplateBinding(__v =>");
		code.ShouldNotContain("ContentChanged");
	}

	private const string TemplateValueTypes = """
		namespace Demo
		{
			public partial class V { }

			public static class Marker
			{
				public static void SetTag(global::SUIsei.UIElement element, string value) { }
			}

			public sealed class Gauge : global::SUIsei.Controls.Control
			{
				public static readonly global::SUIsei.DependencyProperty<Gauge, float> LevelProperty =
					global::SUIsei.DependencyProperty.Register(nameof(Level), new global::SUIsei.PropertyMetadata<Gauge, float>());
				public static readonly global::SUIsei.DependencyProperty<Gauge, int> StepsProperty =
					global::SUIsei.DependencyProperty.Register(nameof(Steps), new global::SUIsei.PropertyMetadata<Gauge, int>());

				public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
				public double Steps { get => GetValue(StepsProperty); set => SetValue(StepsProperty, (int)value); }
			}
		}
		""";

	/// <summary>
	/// In a ControlTemplate an attached property with a dependency property, and a property whose CLR type converts
	/// implicitly to its dependency property's, take the template's value on the part's Template rung, as every other value
	/// a template gives its parts. One the generator can only set through a setter (an attached property with no dependency
	/// property, or a CLR type that does not convert) is reported, as it lands on the part's local value.
	/// </summary>
	[Test]
	public void Template_values_of_attached_and_converted_properties_sit_on_the_template_rung_or_are_reported()
	{
		const string xaml = """
			<ContentControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			                xmlns:demo="using:Demo"
			                x:Class="Demo.V">
			  <ContentControl.Template>
			    <ControlTemplate TargetType="ContentControl">
			      <Grid>
			        <Border Grid.Row="1" demo:Marker.Tag="x" />
			        <Canvas>
			          <Border>
			            <Canvas.Left>5</Canvas.Left>
			          </Border>
			        </Canvas>
			        <demo:Gauge Level="3" Steps="2" />
			      </Grid>
			    </ControlTemplate>
			  </ContentControl.Template>
			</ContentControl>
			""";
		var result = XamlGeneratorHarness.Run(xaml, TemplateValueTypes);
		var code = result.AllGeneratedText();

		code.ShouldContain("global::SUIsei.Controls.Grid.RowProperty, ");
		code.ShouldContain("global::SUIsei.Controls.Canvas.LeftProperty, ");
		code.ShouldContain("global::Demo.Gauge.LevelProperty, (float)(");
		code.ShouldNotContain("Grid.SetRow(");
		code.ShouldNotContain("Canvas.SetLeft(");
		code.ShouldContain("global::Demo.Marker.SetTag(");
		code.ShouldContain(".Steps = ");
		result.DiagnosticIds().ShouldBe(["SUISEI027", "SUISEI027"]);
		XamlGeneratorHarness.CompileErrors(xaml, TemplateValueTypes).ShouldBeEmpty();
	}

	private const string DependencyTemplateBindingXaml = """
		<ContentControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		                x:Class="Demo.V">
		  <ContentControl.Template>
		    <ControlTemplate TargetType="ContentControl">
		      <Border x:Name="Chrome" Opacity="{TemplateBinding Opacity}" />
		    </ControlTemplate>
		  </ContentControl.Template>
		</ContentControl>
		""";

	[Test]
	public void Template_binding_to_a_dependency_property_writes_the_parts_template_rung()
	{
		var result = XamlGeneratorHarness.Run(DependencyTemplateBindingXaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		// The target resolves to its dependency property, so the push lands on the part's Template rung rather than
		// through its CLR setter (Local).
		code.ShouldContain("__tc.RegisterTemplateBinding(global::SUIsei.UIElement.OpacityProperty, Chrome, global::SUIsei.UIElement.OpacityProperty);");
		code.ShouldNotContain("Chrome.Opacity =");
		XamlGeneratorHarness.CompileErrors(DependencyTemplateBindingXaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Template_binding_between_value_types_converts_on_the_way_to_the_template_rung()
	{
		const string xaml = """
			<Slider xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			        x:Class="Demo.V">
			  <Slider.Template>
			    <ControlTemplate TargetType="Slider">
			      <TextBlock x:Name="Readout" FontSize="{TemplateBinding Value}" />
			    </ControlTemplate>
			  </Slider.Template>
			</Slider>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("__tc.RegisterTemplateBinding(global::SUIsei.Controls.Slider.ValueProperty, Readout, global::SUIsei.Controls.TextBlock.FontSizeProperty, static __v => (float)__v);");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void Template_binding_to_a_clr_property_writes_through_its_setter()
	{
		const string xaml = """
			<ContentControl xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			                x:Class="Demo.V">
			  <ContentControl.Template>
			    <ControlTemplate TargetType="ContentControl">
			      <Popup x:Name="Flyout" IsLightDismissEnabled="{TemplateBinding IsEnabled}" />
			    </ControlTemplate>
			  </ContentControl.Template>
			</ContentControl>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		// A CLR property has no precedence levels, so it is written as before.
		code.ShouldContain("__tc.RegisterTemplateBinding(global::SUIsei.Controls.Control.IsEnabledProperty, __v => Flyout.IsLightDismissEnabled = (bool)__v);");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
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
	public void A_control_template_is_shared_from_a_resource_or_a_style_and_its_states_come_after_its_parts()
	{
		// As WinUI markup writes it: the template a keyed resource, its states first and named with x:Name, naming a part
		// declared after them; and a style setting a template of its own through <Setter.Value>.
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <StackPanel.Resources>
			    <ControlTemplate x:Key="Tab" TargetType="ContentControl">
			      <Border>
			        <VisualStateManager.VisualStateGroups>
			          <VisualStateGroup x:Name="Emphasis">
			            <VisualState x:Name="Lit">
			              <Storyboard>
			                <ColorAnimation Storyboard.TargetName="Label" Storyboard.TargetProperty="(ContentPresenter.Foreground).(SolidColorBrush.Color)" To="White" />
			              </Storyboard>
			            </VisualState>
			          </VisualStateGroup>
			        </VisualStateManager.VisualStateGroups>
			        <ContentPresenter x:Name="Label" Content="{TemplateBinding Content}" Foreground="Gray" />
			      </Border>
			    </ControlTemplate>
			    <Style x:Key="Tabbed" TargetType="ContentControl">
			      <Setter Property="Template">
			        <Setter.Value>
			          <ControlTemplate><ContentPresenter Content="{TemplateBinding Content}" /></ControlTemplate>
			        </Setter.Value>
			      </Setter>
			    </Style>
			  </StackPanel.Resources>
			  <ContentControl Template="{StaticResource Tab}" />
			  <ContentControl Style="{StaticResource Tabbed}" />
			</StackPanel>
			""";
		var result = XamlGeneratorHarness.Run(xaml, Vm);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".Add(\"Tab\", new global::SUIsei.Controls.ControlTemplate { Factory = __Template");
		code.ShouldContain("Property = global::SUIsei.Controls.Control.TemplateProperty, Value = new global::SUIsei.Controls.ControlTemplate { Factory = __Template");
		code.ShouldContain("new global::SUIsei.Animation.VisualStateGroup { Name = \"Emphasis\" }");
		code.ShouldContain("new global::SUIsei.Animation.VisualState { Name = \"Lit\"");
		XamlGeneratorHarness.CompileErrors(xaml, Vm).ShouldBeEmpty();
	}

	[Test]
	public void A_template_where_none_can_go_diagnoses()
	{
		// A ControlTemplate set by a style on a property that holds none, and a DataTemplate kept as a resource.
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <StackPanel.Resources>
			    <Style x:Key="S" TargetType="ContentControl">
			      <Setter Property="Background">
			        <Setter.Value><ControlTemplate><Border /></ControlTemplate></Setter.Value>
			      </Setter>
			    </Style>
			    <DataTemplate x:Key="D"><TextBlock /></DataTemplate>
			  </StackPanel.Resources>
			</StackPanel>
			""";
		XamlGeneratorHarness.Run(xaml, Vm).DiagnosticIds().Count(id => id == "SUISEI020").ShouldBe(2);
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
