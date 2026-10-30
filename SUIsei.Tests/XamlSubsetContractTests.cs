using Microsoft.CodeAnalysis;

namespace SUIsei.Tests;

/// <summary>
/// The published XAML subset, the contract in Planning.md, as a test. Every form it lists as <b>In</b> appears here as
/// markup that must generate with no diagnostic at all and compile. Every form it lists as <b>planned</b>, and every WinUI
/// form it lists as <b>Out</b>, must be reported as an error, so no form can pass by being silently dropped. A form added
/// to the contract belongs in one of the three tables.
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
		    <VisualStateGroup x:Name="G">
		      <VisualState x:Name="A" />
		      <VisualState x:Name="B"><Storyboard>{{animations}}</Storyboard></VisualState>
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
		("ControlTemplate as a resource and from a style", """<StackPanel.Resources><ControlTemplate x:Key="Tab" TargetType="ContentControl"><ContentPresenter Content="{TemplateBinding Content}" FontSize="20" /></ControlTemplate><Style x:Key="Tabbed" TargetType="ContentControl"><Setter Property="Template"><Setter.Value><ControlTemplate><ContentPresenter Content="{TemplateBinding Content}" /></ControlTemplate></Setter.Value></Setter></Style></StackPanel.Resources><ContentControl Template="{StaticResource Tab}" /><ContentControl Style="{StaticResource Tabbed}" />"""),
		("visual states before the parts they name, with x:Name", """<ContentControl><ContentControl.Template><ControlTemplate TargetType="ContentControl"><Border><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState x:Name="A" /><VisualState x:Name="B"><Storyboard><ColorAnimation Storyboard.TargetName="Label" Storyboard.TargetProperty="(ContentPresenter.Foreground).(SolidColorBrush.Color)" To="White" /></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups><ContentPresenter x:Name="Label" Content="{TemplateBinding Content}" Foreground="Gray" /></Border></ControlTemplate></ContentControl.Template></ContentControl>"""),
		("inherited text properties and TextAlignment", """<ContentControl FontSize="18" Foreground="Gold"><TextBlock TextAlignment="Center" Text="a&#10;b" /></ContentControl><ContentPresenter FontSize="12" Foreground="Teal" /><TextBox FontSize="16" />"""),
		("ItemsPanelTemplate", """<ItemsControl><ItemsControl.ItemsPanel><ItemsPanelTemplate><VirtualizingStackPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>"""),
		("ResourceDictionary with inline merged dictionaries", """<StackPanel.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary><Style x:Key="M" TargetType="Border" /></ResourceDictionary></ResourceDictionary.MergedDictionaries></ResourceDictionary></StackPanel.Resources>"""),
		("Grid row and column definitions", """<Grid><Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="2*" /></Grid.RowDefinitions><Grid.ColumnDefinitions><ColumnDefinition Width="40" /></Grid.ColumnDefinitions></Grid>"""),
		("DoubleAnimation", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="0:0:1" />""")),
		("ColorAnimation", States("""<ColorAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="Blue" />""")),
		("DoubleAnimationUsingKeyFrames", States("""<DoubleAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Opacity)"><LinearDoubleKeyFrame KeyTime="0:0:0" Value="1" /><DiscreteDoubleKeyFrame KeyTime="0:0:0.5" Value="0.5" /><EasingDoubleKeyFrame KeyTime="0:0:1" Value="0"><EasingDoubleKeyFrame.EasingFunction><QuadraticEase /></EasingDoubleKeyFrame.EasingFunction></EasingDoubleKeyFrame></DoubleAnimationUsingKeyFrames>""")),
		("easing functions", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Left)" To="5"><DoubleAnimation.EasingFunction><BackEase Amplitude="0.3" /></DoubleAnimation.EasingFunction></DoubleAnimation><DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Top)" To="5"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction></DoubleAnimation>""")),
		("WinUI's other easing functions", States(string.Concat(
			new[] { """<SineEase EasingMode="EaseInOut" />""", "<QuarticEase />", "<QuinticEase />", "<CircleEase />", """<PowerEase Power="3" />""", """<ExponentialEase Exponent="4" />""", """<ElasticEase Oscillations="2" Springiness="4" />""", """<BounceEase Bounces="2" Bounciness="3" />""" }
				.Select(static ease => $"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0"><DoubleAnimation.EasingFunction>{ease}</DoubleAnimation.EasingFunction></DoubleAnimation>""")))),
		("ObjectAnimationUsingKeyFrames", States("""<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Visibility)"><DiscreteObjectKeyFrame KeyTime="0:0:0.25" Value="Collapsed" /></ObjectAnimationUsingKeyFrames><ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background)"><ObjectAnimationUsingKeyFrames.KeyFrames><DiscreteObjectKeyFrame KeyTime="0" Value="Blue" /><DiscreteObjectKeyFrame KeyTime="0:0:0.5" Value="{x:Null}" /><DiscreteObjectKeyFrame KeyTime="0:0:1"><DiscreteObjectKeyFrame.Value><SolidColorBrush Color="Red" /></DiscreteObjectKeyFrame.Value></DiscreteObjectKeyFrame></ObjectAnimationUsingKeyFrames.KeyFrames></ObjectAnimationUsingKeyFrames>""")),
		("transform property paths", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateX)" To="5" /><DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)" To="5" />""")),
		("brush property paths", States("""<ColorAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="Blue" /><DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background).(Brush.Opacity)" To="0.5" />""")),
		("brushes from colour strings", """<Border Background="#FF0000" BorderBrush="Gold" /><TextBlock Foreground="CornflowerBlue" /><Button Background="#202020" Foreground="White" BorderBrush="Gray" />"""),
		("image sources bound from code", """<Image Source="{x:Bind Picture}" /><Image Source="{x:Bind Live}" /><Ellipse><Ellipse.Fill><ImageBrush ImageSource="{x:Bind Live}" /></Ellipse.Fill></Ellipse><Rectangle><Rectangle.Fill><ImageBrush ImageSource="{x:Bind Minimap}" Stretch="Uniform" /></Rectangle.Fill></Rectangle>"""),
		("brush elements", """<Border><Border.Background><SolidColorBrush Color="Red" Opacity="0.5" /></Border.Background></Border><Border><Border.Background><LinearGradientBrush StartPoint="0,0" EndPoint="1,0" SpreadMethod="Reflect" MappingMode="Absolute" ColorInterpolationMode="ScRgbLinearInterpolation"><GradientStop Color="Red" Offset="0" /><GradientStop Color="Blue" Offset="1" /></LinearGradientBrush></Border.Background></Border><TextBlock><TextBlock.Foreground><LinearGradientBrush><LinearGradientBrush.GradientStops><GradientStop Color="Red" /><GradientStop Color="Blue" Offset="1" /></LinearGradientBrush.GradientStops></LinearGradientBrush></TextBlock.Foreground></TextBlock><Border><Border.Background><ImageBrush Stretch="UniformToFill" AlignmentX="Left" AlignmentY="Bottom" /></Border.Background></Border>"""),
		("brush resources", """<StackPanel.Resources><Style x:Key="Tinted" TargetType="Border"><Setter Property="Background" Value="Teal" /><Setter Property="BorderBrush"><Setter.Value><SolidColorBrush Color="Gold" /></Setter.Value></Setter></Style></StackPanel.Resources><Border Style="{StaticResource Tinted}" />"""),
		("transforms", """<Border RenderTransformOrigin="0.5,0.5"><Border.RenderTransform><TransformGroup><TranslateTransform X="1" Y="2" /><ScaleTransform ScaleX="2" CenterX="5" /><RotateTransform Angle="45" /><SkewTransform AngleX="10" /><CompositeTransform Rotation="5" TranslateX="3" /><MatrixTransform Matrix="1,0,0,1,4,5" /></TransformGroup></Border.RenderTransform></Border>"""),
		("CornerRadius and BrushTransition", """<Border CornerRadius="4"><Border.BackgroundTransition><BrushTransition Duration="0:0:0.2" /></Border.BackgroundTransition></Border>"""),
		("XML namespaces: suisei: and using:", """<local:Item2Host /><Border suisei:Glow.Radius="2" />"""),
		("mc:Ignorable design-time markup", """<Border d:Width="100" Width="10"><d:Border.Padding>4</d:Border.Padding></Border>"""),
		("suisei: property path owner", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(suisei:Glow.Radius)" To="5" />""")),
		("UIElement.Clip", """<Border><Border.Clip><RectangleGeometry Rect="0,0,10,10" /></Border.Clip></Border>"""),
		("Geometry.Transform", """<Border><Border.Clip><RectangleGeometry Rect="0,0,10,10"><RectangleGeometry.Transform><TranslateTransform X="2" /></RectangleGeometry.Transform></RectangleGeometry></Border.Clip></Border>"""),
		("geometry transform property path", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Clip).(Geometry.Transform).(TranslateTransform.X)" To="5" />""")),
		("geometry elements", """<local:Shape2><local:Shape2.Data><GeometryGroup FillRule="Nonzero"><PathGeometry><PathFigure StartPoint="0,0" IsClosed="True"><LineSegment Point="1,0" /><PolyLineSegment Points="1,1 0,1" /><BezierSegment Point1="0,0" Point2="1,1" Point3="2,0" /><PolyBezierSegment Points="0,0 1,1 2,0" /><QuadraticBezierSegment Point1="1,1" Point2="2,0" /><PolyQuadraticBezierSegment Points="1,1 2,0" /><ArcSegment Point="3,3" Size="2,2" RotationAngle="10" IsLargeArc="True" SweepDirection="Clockwise" /></PathFigure></PathGeometry><RectangleGeometry Rect="0,0,2,2" /><EllipseGeometry Center="1,1" RadiusX="1" RadiusY="2" /><LineGeometry StartPoint="0,0" EndPoint="1,1" /></GeometryGroup></local:Shape2.Data></local:Shape2>"""),
		("path markup, point and number lists", """<local:Shape2 Data="F1 M 0,0 L 10,0 10,10 H 0 V 5 C 1,1 2,2 3,3 S 4,4 5,5 Q 1,1 2,2 T 3,3 A 2,2 0 0 1 4,4 Z m 1,1 l 1,1 z" Points="0,0 1,1" Dashes="2 1" />"""),
		("shapes and the stroke set", """<Canvas><Rectangle Width="10" Height="10" RadiusX="2" RadiusY="2" Fill="Red" Stroke="Blue" StrokeThickness="2" Stretch="Uniform" /><Ellipse Width="10" Height="10" Fill="Red" /><Line X1="0" Y1="0" X2="10" Y2="10" Stroke="Red" StrokeDashArray="2 1" StrokeDashOffset="1" StrokeDashCap="Round" /><Polyline Points="0,0 5,5 10,0" Stroke="Red" StrokeLineJoin="Bevel" StrokeMiterLimit="3" StrokeStartLineCap="Square" StrokeEndLineCap="Triangle" /><Polygon Points="0,0 5,5 10,0" Fill="Red" FillRule="Nonzero" /><Path Data="M 0,0 L 10,10" Stroke="Red" Stretch="UniformToFill" /><Path Stroke="Red"><Path.Data><EllipseGeometry RadiusX="2" RadiusY="2" /></Path.Data></Path></Canvas>"""),
		("shape property paths", """<Canvas><Rectangle x:Name="Card" Width="10" Height="10" Fill="Red" /><Line x:Name="Rule" X2="10" Stroke="Red" /><Path x:Name="Arrow" Data="M 0,0 L 10,10" Stroke="Red" /></Canvas><ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState x:Name="A" /><VisualState x:Name="B"><Storyboard><ColorAnimation Storyboard.TargetName="Card" Storyboard.TargetProperty="(Shape.Fill).(SolidColorBrush.Color)" To="Blue" /><DoubleAnimation Storyboard.TargetName="Card" Storyboard.TargetProperty="(Rectangle.RadiusX)" To="4" /><DoubleAnimation Storyboard.TargetName="Rule" Storyboard.TargetProperty="(Line.X2)" To="20" /><DoubleAnimation Storyboard.TargetName="Rule" Storyboard.TargetProperty="(Shape.StrokeThickness)" To="3" /><DoubleAnimation Storyboard.TargetName="Arrow" Storyboard.TargetProperty="(Path.Data).(Geometry.Transform).(RotateTransform.Angle)" To="90" /><DoubleAnimation Storyboard.TargetName="Rule" Storyboard.TargetProperty="(suisei:StrokeTrim.End)" To="1" /></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>"""),
		("the runtime's suisei: extensions","""<Border suisei:Clipping.ClipToBounds="True" /><ScrollViewer suisei:Scrolling.WheelScrollAmount="64" /><ItemsControl><ItemsControl.ItemsPanel><ItemsPanelTemplate><VirtualizingStackPanel suisei:Virtualization.RowHeight="32" /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl><Path Data="M 0,0 L 10,0" Stroke="Red" suisei:StrokeTrim.Start="0.1" suisei:StrokeTrim.End="0.9" suisei:StrokeTrim.Offset="0.2" suisei:StrokeBrushMapping.Mode="AlongLength" />"""),
		("Typography.NumeralAlignment", """<TextBlock Text="12" Typography.NumeralAlignment="Tabular" />"""),
		("Width and Height Auto, as a value and as an animation's key", """<Border Width="Auto" Height="Auto" />""" + States("""<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(FrameworkElement.Width)"><DiscreteObjectKeyFrame KeyTime="0" Value="Auto" /></ObjectAnimationUsingKeyFrames>""")),
		("durations in WinUI's grammar", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="0:0:0.25" BeginTime="00:00:01.5" /><DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Left)" To="5" Duration="0" />""")),
		("suisei:TextEffects", """<TextBlock x:Name="Title" Text="Go" suisei:TextEffects.OutlineThickness="0.08" suisei:TextEffects.OutlineBrush="Black" suisei:TextEffects.ShadowOffset="0.05,0.1" suisei:TextEffects.ShadowSoftness="0.2" suisei:TextEffects.ShadowBrush="#80000000" /><ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState x:Name="A" /><VisualState x:Name="B"><Storyboard><DoubleAnimation Storyboard.TargetName="Title" Storyboard.TargetProperty="(suisei:TextEffects.OutlineThickness)" To="0.12" /><ColorAnimation Storyboard.TargetName="Title" Storyboard.TargetProperty="(suisei:TextEffects.OutlineBrush).(SolidColorBrush.Color)" To="Red" /></Storyboard></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>"""),
	];

	// Planning §4 "Planned": named in the contract but not implemented yet. Each must be reported, not ignored.
	private static readonly (string Form, string Body, string Id)[] PlannedForms =
	[
		("{ThemeResource}", """<StackPanel.Resources><Style x:Key="T" TargetType="Border" /></StackPanel.Resources><Border Style="{ThemeResource T}" />""", "SUISEI007"),
		("ResourceDictionary Source", """<StackPanel.Resources><ResourceDictionary Source="Theme.xaml" /></StackPanel.Resources>""", "SUISEI020"),
	];

	// The WinUI forms the contract lists as Out, and forms whose meaning differs from WinUI's: each must be an error at its
	// line, never skipped.
	private static readonly (string Form, string Body, string Id)[] OutForms =
	[
		("x:Uid", """<TextBlock x:Uid="Greeting" Text="hi" />""", "SUISEI025"),
		("x:Phase", """<TextBlock x:Phase="2" Text="hi" />""", "SUISEI025"),
		("x:Load", """<TextBlock x:Load="False" Text="hi" />""", "SUISEI025"),
		("x:DeferLoadStrategy", """<TextBlock x:DeferLoadStrategy="Lazy" Text="hi" />""", "SUISEI025"),
		("x:Class on an element other than the root", """<TextBlock x:Class="Demo.Other" />""", "SUISEI025"),
		("x: directive on an animation", States("""<DoubleAnimation x:Uid="Fade" Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" />"""), "SUISEI025"),
		("Name= on a visual state", """<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState Name="A" /></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>""", "SUISEI025"),
		("Name= on a visual state group", """<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup Name="G"><VisualState x:Name="A" /></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>""", "SUISEI025"),
		("VisualState.Setters", """<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState x:Name="A"><VisualState.Setters><Setter Target="X.Opacity" Value="0" /></VisualState.Setters></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>""", "SUISEI025"),
		("VisualState.StateTriggers", """<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState x:Name="A"><VisualState.StateTriggers><AdaptiveTrigger MinWindowWidth="720" /></VisualState.StateTriggers></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>""", "SUISEI025"),
		("VisualStateGroup.Transitions", """<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualStateGroup.Transitions><VisualTransition /></VisualStateGroup.Transitions><VisualState x:Name="A" /></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>""", "SUISEI025"),
		("timing on a Storyboard", """<ContentControl><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="G"><VisualState x:Name="A"><Storyboard RepeatBehavior="Forever" /></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups></ContentControl>""", "SUISEI025"),
		("RowDefinition MinHeight", """<Grid><Grid.RowDefinitions><RowDefinition Height="*" MinHeight="40" /></Grid.RowDefinitions></Grid>""", "SUISEI025"),
		("a second root in a template", """<ContentControl><ContentControl.Template><ControlTemplate><Border /><Border /></ControlTemplate></ContentControl.Template></ContentControl>""", "SUISEI025"),
		("a duration as a bare number (days in WinUI)", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="2" />"""), "SUISEI012"),
		("a duration as m:s (hours and minutes in WinUI)", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="0:30" />"""), "SUISEI012"),
		("a duration that is not a number", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="NaN" />"""), "SUISEI012"),
		("a negative duration", States("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" Duration="-0:0:1" />"""), "SUISEI012"),
		("a child its parent's content can't take", """<StackPanel><SolidColorBrush Color="Red" /></StackPanel>""", "SUISEI026"),
		("an element of an abstract type", """<Border><Border.Background><Brush /></Border.Background></Border>""", "SUISEI025"),
		("an element with no public parameterless constructor", """<Image><Image.Source><suisei:HostImageSource /></Image.Source></Image>""", "SUISEI025"),
		("a property with no public setter", """<ListView SelectedIndex="2" />""", "SUISEI004"),
	];

	[Test]
	public void Every_out_form_is_an_error_at_its_line()
	{
		var failures = new List<string>();
		foreach (var (form, body, id) in OutForms)
		{
			var diagnostics = XamlGeneratorHarness.Run(View(body), CodeBehind).Diagnostics;
			if (diagnostics.Select(d => d.Id).ToArray() is not [var only] || only != id || diagnostics[0].Severity != DiagnosticSeverity.Error)
				failures.Add($"{form}: expected one {id} error, got {string.Join("; ", diagnostics.Select(d => $"{d.Id} {d.GetMessage()}"))}");
		}
		failures.ShouldBeEmpty();
	}

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

	// Controls in the using: namespace: one for the XML-namespace form, and one with the geometry value types for the
	// geometry forms, which no runtime element takes yet. The view's image sources, which markup binds and code makes.
	private const string ExtraTypes = """
		namespace Demo
		{
			public partial class V
			{
				public SUIsei.Media.Imaging.BitmapSource Picture { get; } = SUIsei.Media.Imaging.BitmapSource.FromRgba8(1, 1, new byte[4]);
				public SUIsei.Media.Imaging.WriteableBitmap Live { get; } = new(2, 2);
				public SUIsei.Extensions.HostImageSource Minimap { get; } = new("minimap", 32, 16);
			}
			public sealed class Item2Host : SUIsei.Controls.ContentControl { }
			public sealed class Shape2 : SUIsei.Controls.ContentControl
			{
				public SUIsei.Media.Geometry? Data { get; set; }
				public SUIsei.Media.PointCollection Points { get; set; } = new();
				public SUIsei.Media.DoubleCollection Dashes { get; set; } = new();
			}
		}
		""";
}
