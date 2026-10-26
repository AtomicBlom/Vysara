using Microsoft.CodeAnalysis;

namespace SUIsei.Tests;

/// <summary>
/// The published XAML subset (Planning.md §4) as a test. Every form §4 lists as <b>In</b> appears here as markup that
/// must generate with no diagnostic at all (in particular no SUISEI007 "unsupported markup") and compile. Every form
/// §4 lists as <b>planned</b> must still be reported, so a planned form can never pass by being silently dropped. A
/// form added to §4 belongs in one of the two tables.
/// </summary>
public sealed class XamlSubsetContractTests
{
	private const string CodeBehind = """
		namespace Demo
		{
			public partial class V
			{
				public Vm ViewModel { get; } = new();
				public void OnClick(object sender, SUIsei.RoutedEvents.RoutedEventArgs e) { }
			}
			public sealed class Vm : System.ComponentModel.INotifyPropertyChanged
			{
				public string? Caption { get; set; }
				public Inner Child { get; } = new();
				public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
			}
			public sealed class Inner : System.ComponentModel.INotifyPropertyChanged
			{
				public string Name { get; set; } = "";
				public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
			}
			public sealed class Item { public string Title { get; set; } = ""; }
			public static class Palette { public const string Title = "Title"; }
		}
		namespace SUIsei.Extensions
		{
			public sealed class Glow : SUIsei.DependencyObject
			{
				public static readonly SUIsei.DependencyProperty<Glow, float> RadiusProperty =
					SUIsei.DependencyProperty.RegisterAttached<Glow, float>("Radius", new SUIsei.PropertyMetadata<Glow, float>());
				public static void SetRadius(SUIsei.UIElement element, float value) => element.SetValue(RadiusProperty, value);
			}
		}
		""";

	private static string View(string body) => $$"""
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            xmlns:suisei="https://github.com/AtomicBlom/SUIsei/extensions"
		            xmlns:local="using:Demo"
		            xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
		            xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
		            mc:Ignorable="d"
		            x:Class="Demo.V">
		  {{body}}
		</StackPanel>
		""";

	// A visual state on a named Border "Box" (inside a Canvas) whose storyboard holds `animations`.
	private static string States(string animations) => $$"""
		<Canvas><Border x:Name="Box" Width="10" Height="10" Background="Red" /></Canvas>
		<ContentControl>
		  <VisualStateManager.VisualStateGroups>
		    <VisualStateGroup Name="G">
		      <VisualState Name="A" />
		      <VisualState Name="B"><Storyboard>{{animations}}</Storyboard></VisualState>
		    </VisualStateGroup>
		  </VisualStateManager.VisualStateGroups>
		</ContentControl>
		""";

	// Planning §4 "In".
	private static readonly (string Form, string Body)[] InForms =
	[
		("elements and attributes", """<Border Width="10" Background="#FF0000" HorizontalAlignment="Left" />"""),
		("x:Class and x:Name", """<TextBlock x:Name="Named" Text="hi" />"""),
		("x:Key", """<StackPanel.Resources><Style x:Key="K" TargetType="Border" /></StackPanel.Resources>"""),
		("x:DataType", """<ItemsControl><ItemsControl.ItemTemplate><DataTemplate x:DataType="local:Item"><TextBlock Text="{x:Bind Title}" /></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>"""),
		("property element: value", """<Border><Border.Padding>1,2</Border.Padding></Border>"""),
		("property element: object", """<Border><Border.Child><TextBlock /></Border.Child></Border>"""),
		("property element: collection", """<StackPanel><StackPanel.Children><TextBlock /><TextBlock /></StackPanel.Children></StackPanel>"""),
		("property element: attached", """<Canvas><Border><Canvas.Left>4</Canvas.Left></Border></Canvas>"""),
		("content property", """<Border><TextBlock /></Border>"""),
		("attached property", """<Grid><Border Grid.Row="1" Grid.Column="0" /></Grid>"""),
		("event handler", """<Button Click="OnClick" />"""),
		("{x:Bind} OneTime", """<TextBlock Text="{x:Bind ViewModel.Caption}" />"""),
		("{x:Bind} OneWay deep path", """<TextBlock Text="{x:Bind ViewModel.Child.Name, Mode=OneWay}" />"""),
		("{x:Bind} TwoWay", """<TextBox Text="{x:Bind ViewModel.Caption, Mode=TwoWay}" />"""),
		("{x:Bind} FallbackValue and TargetNullValue", """<TextBlock Text="{x:Bind ViewModel.Caption, Mode=OneWay, FallbackValue=N/A, TargetNullValue=none}" />"""),
		("{StaticResource}", """<StackPanel.Resources><Style x:Key="S" TargetType="Border" /></StackPanel.Resources><Border Style="{StaticResource S}" />"""),
		("{TemplateBinding}", """<ContentControl><ContentControl.Template><ControlTemplate TargetType="ContentControl"><ContentPresenter Content="{TemplateBinding Content}" /></ControlTemplate></ContentControl.Template></ContentControl>"""),
		("{x:Null}", """<TextBlock Text="{x:Null}" />"""),
		("{x:Static}", """<TextBlock Text="{x:Static local:Palette.Title}" />"""),
		("Style, Setter and BasedOn", """<StackPanel.Resources><Style x:Key="A" TargetType="TextBlock"><Setter Property="FontSize" Value="12" /></Style><Style x:Key="B" TargetType="TextBlock" BasedOn="{StaticResource A}"><Setter Property="Canvas.Left"><Setter.Value>3</Setter.Value></Setter></Style></StackPanel.Resources>"""),
		("DataTemplate", """<ContentPresenter><ContentPresenter.ContentTemplate><DataTemplate><TextBlock /></DataTemplate></ContentPresenter.ContentTemplate></ContentPresenter>"""),
		("ControlTemplate", """<ContentControl><ContentControl.Template><ControlTemplate><Border /></ControlTemplate></ContentControl.Template></ContentControl>"""),
		("ItemsPanelTemplate", """<ItemsControl><ItemsControl.ItemsPanel><ItemsPanelTemplate><VirtualizingStackPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>"""),
		("ResourceDictionary with inline merged dictionaries", """<StackPanel.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary><Style x:Key="M" TargetType="Border" /></ResourceDictionary></ResourceDictionary.MergedDictionaries></ResourceDictionary></StackPanel.Resources>"""),
		("Grid row and column definitions", """<Grid><Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="2*" /></Grid.RowDefinitions><Grid.ColumnDefinitions><ColumnDefinition Width="40" /></Grid.ColumnDefinitions></Grid>"""),
		("DoubleAnimation", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="0:0:1" />""")),
		("ColorAnimation", States("""<ColorAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="Blue" />""")),
		("DoubleAnimationUsingKeyFrames", States("""<DoubleAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Opacity)"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="1" /><DiscreteDoubleKeyFrame KeyTime="0:0:0.5" Value="0.5" /><EasingDoubleKeyFrame KeyTime="0:0:1" Value="0"><EasingDoubleKeyFrame.EasingFunction><QuadraticEase /></EasingDoubleKeyFrame.EasingFunction></EasingDoubleKeyFrame></DoubleAnimationUsingKeyFrames>""")),
		("easing functions", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Left)" To="5"><DoubleAnimation.EasingFunction><BackEase Amplitude="0.3" /></DoubleAnimation.EasingFunction></DoubleAnimation><DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Top)" To="5"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction></DoubleAnimation>""")),
		("transform property paths", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateX)" To="5" /><DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="RenderTransform.Translate.X" To="5" />""")),
		("XML namespaces: suisei: and using:", """<local:Item2Host /><Border suisei:Glow.Radius="2" />"""),
		("mc:Ignorable design-time markup", """<Border d:Width="100" Width="10"><d:Border.Padding>4</d:Border.Padding></Border>"""),
		("suisei: property path owner", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(suisei:Glow.Radius)" To="5" />""")),
		("the runtime's suisei: extensions", """<Border suisei:Clipping.ClipToBounds="True" /><ScrollViewer suisei:Scrolling.WheelScrollAmount="64" /><ItemsControl><ItemsControl.ItemsPanel><ItemsPanelTemplate><VirtualizingStackPanel suisei:Virtualization.RowHeight="32" /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>"""),
	];

	// Planning §4 "Planned": named in the contract but not implemented yet. Each must be reported, not ignored.
	private static readonly (string Form, string Body, string Id)[] PlannedForms =
	[
		("{ThemeResource}", """<StackPanel.Resources><Style x:Key="T" TargetType="Border" /></StackPanel.Resources><Border Style="{ThemeResource T}" />""", "SUISEI007"),
		("ResourceDictionary Source", """<StackPanel.Resources><ResourceDictionary Source="Theme.xaml" /></StackPanel.Resources>""", "SUISEI020"),
	];

	[Test]
	public void Every_published_form_generates_with_no_diagnostic_and_compiles()
	{
		var failures = new List<string>();
		foreach (var (form, body) in InForms)
		{
			var xaml = View(body);
			var diagnostics = XamlGeneratorHarness.Run(xaml, CodeBehind + ExtraTypes).Diagnostics;
			if (diagnostics.Length > 0)
			{
				failures.Add($"{form}: {string.Join("; ", diagnostics.Select(d => $"{d.Id} {d.GetMessage()}"))}");
				continue;
			}
			var errors = XamlGeneratorHarness.CompileErrors(xaml, CodeBehind + ExtraTypes);
			if (errors.Length > 0)
				failures.Add($"{form}: does not compile: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
		}
		failures.ShouldBeEmpty();
	}

	[Test]
	public void Every_planned_form_is_reported_not_dropped()
	{
		foreach (var (form, body, id) in PlannedForms)
		{
			var diagnostics = XamlGeneratorHarness.Run(View(body), CodeBehind).Diagnostics;
			diagnostics.Select(d => d.Id).ShouldBe([id], form);
			diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Error, form);
		}
	}

	// A control in the using: namespace, for the XML-namespace form.
	private const string ExtraTypes = """
		namespace Demo { public sealed class Item2Host : SUIsei.Controls.ContentControl { } }
		""";
}
