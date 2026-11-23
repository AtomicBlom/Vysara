namespace Vysara.Tests;

/// <summary>
/// Generator coverage for <c>&lt;VisualStateManager.VisualStateGroups&gt;</c> + <c>&lt;Storyboard&gt;</c> markup:
/// the VSM groups, their state storyboards, each animation with its target, keyframes and easing functions, and
/// <c>Storyboard.TargetProperty</c> paths resolved at compile time to dependency properties, through the brushes and
/// transforms that hold them — no reflection, no runtime string lookup. Asserted on the generated source text, and
/// compiled.
/// </summary>
public sealed class StoryboardGeneratorTests
{
	private const string CodeBehind = "namespace Demo { public partial class V { } }";

	private const string StatesXaml = """
		<Border xmlns="https://github.com/AtomicBlom/Vysara/ui"
		        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		        x:Class="T.StatesView" x:Name="Bg">
		  <VisualStateManager.VisualStateGroups>
		    <VisualStateGroup x:Name="CommonStates">
		      <VisualState x:Name="Normal" />
		      <VisualState x:Name="Highlighted">
		        <Storyboard>
		          <ColorAnimation Storyboard.TargetName="Bg" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)"
		                          To="#5060A0" Duration="0:0:0.12" FillBehavior="HoldEnd" />
		        </Storyboard>
		      </VisualState>
		    </VisualStateGroup>
		  </VisualStateManager.VisualStateGroups>
		</Border>
		""";

	// A view with a Canvas-hosted Border "Box" and a TextBlock "Label", and one visual state whose storyboard holds
	// `animations`.
	private static string View(string animations) => $$"""
		<StackPanel xmlns="https://github.com/AtomicBlom/Vysara/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            x:Class="Demo.V">
		  <Canvas Width="200" Height="100">
		    <Border x:Name="Box" Width="10" Height="10" Background="#FF0000" />
		  </Canvas>
		  <TextBlock x:Name="Label" Text="Hi" />
		  <ContentControl x:Name="Host">
		    <VisualStateManager.VisualStateGroups>
		      <VisualStateGroup x:Name="G">
		        <VisualState x:Name="S">
		          <Storyboard>
		            {{animations}}
		          </Storyboard>
		        </VisualState>
		      </VisualStateGroup>
		    </VisualStateManager.VisualStateGroups>
		  </ContentControl>
		</StackPanel>
		""";

	[Test]
	public void Visual_state_groups_and_storyboards_are_emitted()
	{
		var result = XamlGeneratorHarness.Run(StatesXaml);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("new global::Vysara.Animation.VisualStateGroup { Name = \"CommonStates\" }");
		code.ShouldContain("new global::Vysara.Animation.VisualState { Name = \"Normal\", Storyboard = null }");
		code.ShouldContain("new global::Vysara.Animation.ColorAnimation()");
		code.ShouldContain(".To = global::Vysara.HdrColor.FromRgb(80, 96, 160);");
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(0.12);");
		code.ShouldContain(".FillBehavior = global::Vysara.Animation.FillBehavior.HoldEnd;");
		code.ShouldContain(".Target = Bg;");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Border.BackgroundProperty, global::Vysara.Media.SolidColorBrush.ColorProperty);");
		code.ShouldContain("global::Vysara.Animation.VisualStateManager.SetVisualStateGroups(Bg,");
	}

	[Test]
	public void Owner_qualified_and_attached_paths_resolve_to_dependency_properties()
	{
		var xaml = View("""
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Opacity)" To="0.5" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Left)" To="100" />
			<DoubleAnimation Storyboard.TargetName="Label" Storyboard.TargetProperty="(TextBlock.FontSize)" To="20" />
			""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.UIElement.OpacityProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Canvas.LeftProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.TextBlock.FontSizeProperty);");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	public void WinUI_brush_and_transform_paths_step_through_the_objects_that_hold_them()
	{
		var xaml = View("""
			<ColorAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="#0000FF" />
			<ColorAnimation Storyboard.TargetName="Label" Storyboard.TargetProperty="(TextBlock.Foreground).(SolidColorBrush.Color)" To="#0000FF" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.RenderTransform).(CompositeTransform.TranslateX)" To="5" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)" To="5" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background).(Brush.Opacity)" To="0.5" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Clip).(Geometry.Transform).(TranslateTransform.X)" To="5" />
			""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Border.BackgroundProperty, global::Vysara.Media.SolidColorBrush.ColorProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.TextBlock.ForegroundProperty, global::Vysara.Media.SolidColorBrush.ColorProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.UIElement.RenderTransformProperty, global::Vysara.Media.CompositeTransform.TranslateXProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.UIElement.RenderTransformProperty, global::Vysara.Media.TranslateTransform.YProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Border.BackgroundProperty, global::Vysara.Media.Brush.OpacityProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.UIElement.ClipProperty, global::Vysara.Media.Geometry.TransformProperty, global::Vysara.Media.TranslateTransform.XProperty);");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Background\" To=\"Blue\" />", "(Border.Background).(SolidColorBrush.Color)")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"(Border.Background)\" To=\"Blue\" />", "(Border.Background).(SolidColorBrush.Color)")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Label\" Storyboard.TargetProperty=\"Foreground\" To=\"Blue\" />", "(TextBlock.Foreground).(SolidColorBrush.Color)")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Host\" Storyboard.TargetProperty=\"Background\" To=\"Blue\" />", "(ContentControl.Background).(SolidColorBrush.Color)")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"RenderTransform.Translate.X\" To=\"5\" />", "(UIElement.RenderTransform).(TranslateTransform.X)")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"RenderTransform.Translate.Y\" To=\"5\" />", "(UIElement.RenderTransform).(TranslateTransform.Y)")]
	public void The_animation_shorthand_from_before_brushes_diagnoses_VYSARA023_with_the_WinUI_path(string animation, string suggestion)
	{
		var diagnostic = XamlGeneratorHarness.Run(View(animation), CodeBehind).Diagnostics.Single();
		diagnostic.Id.ShouldBe("VYSARA023");
		diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).ShouldEndWith($"write the WinUI path '{suggestion}'");
	}

	[Test]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"#0000FF\" />")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Background\" To=\"1\" />")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"(Grid.Row)\" To=\"1\" />")]
	public void A_property_the_animation_cannot_drive_diagnoses_VYSARA016(string animation) =>
		XamlGeneratorHarness.Run(View(animation), CodeBehind).DiagnosticIds().ShouldBe(["VYSARA016"]);

	[Test]
	[Arguments("Nonsense")]                                      // no such property
	[Arguments("(Nope.Opacity)")]                                // unknown owner type
	[Arguments("(TextBlock.FontSize)")]                          // a TextBlock property, on a Border
	[Arguments("Foreground")]                                    // a Border has no Foreground
	[Arguments("(UIElement.Opacity).(UIElement.Opacity)")]       // Opacity holds a float, not an object to step into
	[Arguments("(UIElement.Opacity")]                            // malformed
	[Arguments("Opacity.")]                                      // malformed
	public void An_unresolvable_path_diagnoses_VYSARA021(string path) =>
		XamlGeneratorHarness.Run(View($"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="{path}" To="1" />"""), CodeBehind)
			.DiagnosticIds().ShouldBe(["VYSARA021"]);

	[Test]
	public void A_path_diagnostic_points_at_the_target_property_attribute()
	{
		var xaml = View("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Nonsense" To="1" />""");
		var diagnostic = XamlGeneratorHarness.Run(xaml, CodeBehind).Diagnostics.Single();

		var span = diagnostic.Location.GetLineSpan();
		var line = xaml.Split('\n')[span.StartLinePosition.Line];
		line.Substring(span.StartLinePosition.Character).ShouldStartWith("Storyboard.TargetProperty=\"Nonsense\"");
		diagnostic.GetMessage().ShouldBe("Property path 'Nonsense' does not resolve: 'Border' has no dependency property 'Nonsense'");
	}

	private const string ExtensionCodeBehind = """
		namespace Demo { public partial class V { } }
		namespace Vysara.Extensions
		{
			public sealed class Glow : Vysara.DependencyObject
			{
				public static readonly Vysara.DependencyProperty<Glow, float> RadiusProperty =
					Vysara.DependencyProperty.RegisterAttached<Glow, float>("Radius", new Vysara.PropertyMetadata<Glow, float>());
				public static void SetRadius(Vysara.UIElement element, float value) => element.SetValue(RadiusProperty, value);
			}
		}
		""";

	// The default View with the extension namespace declared as `vysara:` and Vysara.Controls as `using:`.
	private static string PrefixedView(string animations) => View(animations).Replace(
		"x:Class=\"Demo.V\"",
		"xmlns:vysara=\"https://github.com/AtomicBlom/Vysara/extensions\" xmlns:ctl=\"using:Vysara.Controls\" x:Class=\"Demo.V\"");

	[Test]
	public void Owner_prefixes_resolve_through_their_XML_namespace()
	{
		var xaml = PrefixedView("""
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(vysara:Glow.Radius)" To="3" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(ctl:Canvas.Top)" To="4" />
			""");
		var result = XamlGeneratorHarness.Run(xaml, ExtensionCodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Extensions.Glow.RadiusProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Canvas.TopProperty);");
		XamlGeneratorHarness.CompileErrors(xaml, ExtensionCodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("(vysara:Nope.Radius)", "VYSARA022")]   // no such extension type
	[Arguments("(nope:Glow.Radius)", "VYSARA021")]     // an undeclared prefix
	[Arguments("(ctl:Glow.Radius)", "VYSARA021")]      // declared, but the type is not in that namespace
	public void An_unresolvable_owner_prefix_diagnoses(string path, string id) =>
		XamlGeneratorHarness.Run(PrefixedView($"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="{path}" To="1" />"""), ExtensionCodeBehind)
			.DiagnosticIds().ShouldBe([id]);

	[Test]
	public void An_attached_property_animated_on_an_object_its_setter_does_not_take_is_VYSARA021()
	{
		var diagnostic = XamlGeneratorHarness.Run(PrefixedView("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(vysara:StrokeTrim.End)" To="1" />"""),
			ExtensionCodeBehind).Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("VYSARA021");
		diagnostic.GetMessage().ShouldContain("is set on a Shape, not a Border");
	}

	[Test]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\" Bogus=\"2\" />", "VYSARA004")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"lots\" />", "VYSARA012")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"(Border.Background).(SolidColorBrush.Color)\" From=\"Blurple\" />", "VYSARA012")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\" Duration=\"later\" />", "VYSARA012")]
	public void A_bad_animation_attribute_is_an_error(string animation, string id) =>
		XamlGeneratorHarness.Run(View(animation), CodeBehind).DiagnosticIds().ShouldBe([id]);

	[Test]
	[Arguments("0:0:0.25", "0.25")]
	[Arguments("00:01:02.5", "62.5")]
	[Arguments("1.02:03", "93780")]
	[Arguments("1.02:03:04.5", "93784.5")]
	[Arguments("0:00", "0")]
	[Arguments(" 0 ", "0")]
	public void Durations_read_in_WinUIs_grammar(string duration, string seconds)
	{
		var xaml = View($"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="1" Duration="{duration}" />""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		result.DiagnosticIds().ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain($".Duration = global::System.TimeSpan.FromSeconds({seconds});");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("2", true)]
	[Arguments("0:30", true)]
	[Arguments("1.5", false)]
	[Arguments("1:02:03:04", false)]
	[Arguments("0:30.5", false)]
	[Arguments("0:0:60", false)]
	[Arguments("24:00:00", false)]
	[Arguments("1.", false)]
	[Arguments("-0:0:1", false)]
	public void A_duration_outside_the_grammar_is_VYSARA012_saying_how_to_write_one_WinUI_reads_otherwise(string duration, bool readOtherwise)
	{
		var diagnostic = XamlGeneratorHarness.Run(View($"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="1" Duration="{duration}" />"""),
			CodeBehind).Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("VYSARA012");
		diagnostic.GetMessage().ShouldContain("h:m:s[.f] or d.h:m[:s[.f]]");
		diagnostic.GetMessage().Contains("WinUI reads a bare number as days").ShouldBe(readOtherwise);
	}

	[Test]
	public void Keyframes_and_easing_functions_are_emitted()
	{
		var xaml = View("""
			<DoubleAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Opacity)" RepeatBehavior="Forever">
			  <LinearDoubleKeyFrame KeyTime="0:0:0" Value="1" />
			  <DiscreteDoubleKeyFrame KeyTime="0:0:0.5" Value="0.5" />
			  <EasingDoubleKeyFrame KeyTime="0:0:2" Value="0.25">
			    <EasingDoubleKeyFrame.EasingFunction>
			      <CubicEase EasingMode="EaseIn" />
			    </EasingDoubleKeyFrame.EasingFunction>
			  </EasingDoubleKeyFrame>
			</DoubleAnimationUsingKeyFrames>
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Left)" To="100">
			  <DoubleAnimation.EasingFunction>
			    <BackEase Amplitude="0.5" EasingMode="EaseInOut" />
			  </DoubleAnimation.EasingFunction>
			</DoubleAnimation>
			""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("new global::Vysara.Animation.DoubleAnimationUsingKeyFrames()");
		code.ShouldContain(".KeyTime = global::System.TimeSpan.FromSeconds(0.5);");
		code.ShouldContain(".IsDiscrete = true;");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.CubicEase { EasingMode = global::Vysara.Animation.EasingMode.EaseIn };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.BackEase { Amplitude = 0.5d, EasingMode = global::Vysara.Animation.EasingMode.EaseInOut };");
		// With no Duration, a keyframe animation lasts until its last key.
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(2);");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\"><LinearDoubleKeyFrame KeyTime=\"soon\" Value=\"1\" /></DoubleAnimationUsingKeyFrames>", "VYSARA012")]
	[Arguments("<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\"><LinearDoubleKeyFrame KeyTime=\"0:30\" Value=\"1\" /></DoubleAnimationUsingKeyFrames>", "VYSARA012")]
	[Arguments("<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\"><SplineDoubleKeyFrame KeyTime=\"0:0:1\" Value=\"1\" /></DoubleAnimationUsingKeyFrames>", "VYSARA020")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\"><DoubleAnimation.EasingFunction><WobbleEase /></DoubleAnimation.EasingFunction></DoubleAnimation>", "VYSARA003")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\"><DoubleAnimation.EasingFunction><CubicEase EasingMode=\"Sideways\" /></DoubleAnimation.EasingFunction></DoubleAnimation>", "VYSARA012")]
	public void Bad_keyframes_and_easing_functions_diagnose(string animation, string id) =>
		XamlGeneratorHarness.Run(View(animation), CodeBehind).DiagnosticIds().ShouldBe([id]);

	[Test]
	public void Object_keyframes_convert_their_values_for_the_property_they_set()
	{
		var xaml = View("""
			<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Visibility)">
			  <DiscreteObjectKeyFrame KeyTime="0:0:0.25" Value="Collapsed" />
			</ObjectAnimationUsingKeyFrames>
			<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background)">
			  <ObjectAnimationUsingKeyFrames.KeyFrames>
			    <DiscreteObjectKeyFrame KeyTime="0" Value="#0000FF" />
			    <DiscreteObjectKeyFrame KeyTime="0:0:0.5" Value="{x:Null}" />
			    <DiscreteObjectKeyFrame KeyTime="0:0:1">
			      <DiscreteObjectKeyFrame.Value>
			        <SolidColorBrush Color="Red" />
			      </DiscreteObjectKeyFrame.Value>
			    </DiscreteObjectKeyFrame>
			  </ObjectAnimationUsingKeyFrames.KeyFrames>
			</ObjectAnimationUsingKeyFrames>
			<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Label" Storyboard.TargetProperty="Text" Duration="0:0:2" FillBehavior="Stop">
			  <DiscreteObjectKeyFrame KeyTime="0:0:1" Value="Bye" />
			</ObjectAnimationUsingKeyFrames>
			<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(Grid.Row)" EnableDependentAnimation="True">
			  <DiscreteObjectKeyFrame KeyTime="0" Value="2" />
			</ObjectAnimationUsingKeyFrames>
			<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(UIElement.Opacity)">
			  <DiscreteObjectKeyFrame KeyTime="0" Value="0.5" />
			</ObjectAnimationUsingKeyFrames>
			""");
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("new global::Vysara.Animation.ObjectAnimationUsingKeyFrames()");
		code.ShouldContain("new global::Vysara.Animation.DiscreteObjectKeyFrame()");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.UIElement.VisibilityProperty);");
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Grid.RowProperty);");
		code.ShouldContain(".Value = global::Vysara.Visibility.Collapsed;");
		code.ShouldContain(".Value = new global::Vysara.Media.SolidColorBrush(global::Vysara.HdrColor.FromRgb(0, 0, 255));");
		code.ShouldContain(".Value = null;");
		code.ShouldContain(".Value = \"Bye\";");
		code.ShouldContain(".Value = 2;");
		code.ShouldContain(".EnableDependentAnimation = true;"); // WinUI markup that sets it compiles unchanged
		code.ShouldContain(".Value = 0.5f;"); // a float for a float property, as the runtime checks
		code.ShouldContain(".Value = __"); // the object element, built as any element is
		code.ShouldContain(".FillBehavior = global::Vysara.Animation.FillBehavior.Stop;");
		// With no Duration, an object keyframe animation lasts until its last key too.
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(0.25);");
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(1);");
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(2);");
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(0);");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	public void An_object_keyframe_value_may_be_a_resource_looked_up_from_the_states_owner()
	{
		var xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/Vysara/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  <Border x:Name="Box" Width="10" Height="10" />
			  <ContentControl x:Name="Host">
			    <ContentControl.Resources>
			      <SolidColorBrush x:Key="Accent" Color="#00FF00" />
			    </ContentControl.Resources>
			    <VisualStateManager.VisualStateGroups>
			      <VisualStateGroup x:Name="G">
			        <VisualState x:Name="S">
			          <Storyboard>
			            <ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="(Border.Background)">
			              <DiscreteObjectKeyFrame KeyTime="0" Value="{StaticResource Accent}" />
			            </ObjectAnimationUsingKeyFrames>
			          </Storyboard>
			        </VisualState>
			      </VisualStateGroup>
			    </VisualStateManager.VisualStateGroups>
			  </ContentControl>
			</StackPanel>
			""";
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);

		result.DiagnosticIds().ShouldBeEmpty();
		result.AllGeneratedText().ShouldContain(".Value = (Host.FindResource(\"Accent\")) as global::Vysara.Media.Brush;");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	private const string ObjectKeys = """<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box" Storyboard.TargetProperty="{0}">{1}</ObjectAnimationUsingKeyFrames>""";

	[Test]
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"Sideways\" />", "VYSARA012")]           // no such member
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"{x:Null}\" />", "VYSARA012")]           // a Visibility cannot be null
	[Arguments("(Grid.Row)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"lots\" />", "VYSARA012")]                         // not a whole number
	[Arguments("(Border.Background)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"Blurple\" />", "VYSARA012")]             // not a colour
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"soon\" Value=\"Collapsed\" />", "VYSARA012")]     // not a time
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" />", "VYSARA020")]                             // no value
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"Visible\"><DiscreteObjectKeyFrame.Value>Collapsed</DiscreteObjectKeyFrame.Value></DiscreteObjectKeyFrame>", "VYSARA020")] // two values
	[Arguments("(Border.Background)", "<DiscreteObjectKeyFrame KeyTime=\"0\"><DiscreteObjectKeyFrame.Value><TranslateTransform /></DiscreteObjectKeyFrame.Value></DiscreteObjectKeyFrame>", "VYSARA020")] // not a brush
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"{x:Bind Nope}\" />", "VYSARA007")]     // markup outside the subset
	[Arguments("(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"Collapsed\" Bogus=\"1\" />", "VYSARA004")]
	[Arguments("(UIElement.Visibility)", "<DiscreteDoubleKeyFrame KeyTime=\"0\" Value=\"1\" />", "VYSARA020")]                // a double key
	[Arguments("(UIElement.Visibility)", "<ObjectAnimationUsingKeyFrames.EasingFunction><SineEase /></ObjectAnimationUsingKeyFrames.EasingFunction>", "VYSARA020")]
	[Arguments("(UIElement.Nope)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"Collapsed\" />", "VYSARA021")]             // reported once, the value left alone
	[Arguments("(UIElement.Visibility).(UIElement.Opacity)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"1\" />", "VYSARA021")]
	public void A_bad_object_keyframe_diagnoses(string path, string keys, string id) =>
		XamlGeneratorHarness.Run(View(string.Format(System.Globalization.CultureInfo.InvariantCulture, ObjectKeys, path, keys)), CodeBehind)
			.DiagnosticIds().ShouldBe([id]);

	[Test]
	public void An_object_keyframe_animation_needs_a_target_property_to_convert_its_values_for()
	{
		var diagnostic = XamlGeneratorHarness.Run(View("""
			<ObjectAnimationUsingKeyFrames Storyboard.TargetName="Box">
			  <DiscreteObjectKeyFrame KeyTime="0" Value="Collapsed" />
			</ObjectAnimationUsingKeyFrames>
			"""), CodeBehind).Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("VYSARA020");
		diagnostic.GetMessage().ShouldContain("needs a Storyboard.TargetProperty");
	}

	[Test]
	public void A_bad_object_keyframe_value_names_the_property_and_its_type()
	{
		var diagnostic = XamlGeneratorHarness.Run(View(string.Format(System.Globalization.CultureInfo.InvariantCulture, ObjectKeys,
			"(UIElement.Visibility)", "<DiscreteObjectKeyFrame KeyTime=\"0\" Value=\"Sideways\" />")), CodeBehind).Diagnostics.ShouldHaveSingleItem();
		diagnostic.GetMessage().ShouldBe("'Sideways' is not a valid value for property '(UIElement.Visibility)' of type 'Vysara.Visibility': expected a member of Visibility");
	}

	[Test]
	public void WinUIs_easing_functions_are_emitted_with_their_parameters()
	{
		static string Eased(string property, string ease) => $"""
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="{property}" To="1">
			  <DoubleAnimation.EasingFunction>{ease}</DoubleAnimation.EasingFunction>
			</DoubleAnimation>
			""";
		var xaml = View(string.Concat(
			Eased("Opacity", """<SineEase EasingMode="EaseInOut" />"""),
			Eased("(Canvas.Left)", """<QuarticEase />"""),
			Eased("(Canvas.Top)", """<QuinticEase EasingMode="EaseIn" />"""),
			Eased("Opacity", """<CircleEase />"""),
			Eased("Opacity", """<PowerEase Power="3.5" />"""),
			Eased("Opacity", """<ExponentialEase Exponent="6" EasingMode="EaseInOut" />"""),
			Eased("Opacity", """<ElasticEase Oscillations="5" Springiness="1.5" />"""),
			Eased("Opacity", """<BounceEase Bounces="2" Bounciness="3" EasingMode="EaseIn" />""")));
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.SineEase { EasingMode = global::Vysara.Animation.EasingMode.EaseInOut };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.QuarticEase {  };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.QuinticEase { EasingMode = global::Vysara.Animation.EasingMode.EaseIn };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.CircleEase {  };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.PowerEase { Power = 3.5d };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.ExponentialEase { Exponent = 6d, EasingMode = global::Vysara.Animation.EasingMode.EaseInOut };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.ElasticEase { Oscillations = 5, Springiness = 1.5d };");
		code.ShouldContain(".EasingFunction = new global::Vysara.Animation.BounceEase { Bounces = 2, Bounciness = 3d, EasingMode = global::Vysara.Animation.EasingMode.EaseIn };");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("<ElasticEase Oscillations=\"2.5\" />", "VYSARA012")] // a whole number of swings
	[Arguments("<BounceEase Bounciness=\"springy\" />", "VYSARA012")]
	[Arguments("<PowerEase Exponent=\"3\" />", "VYSARA004")]        // ExponentialEase's, not PowerEase's
	public void A_bad_easing_parameter_diagnoses(string ease, string id) =>
		XamlGeneratorHarness.Run(View($"""
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="1">
			  <DoubleAnimation.EasingFunction>{ease}</DoubleAnimation.EasingFunction>
			</DoubleAnimation>
			"""), CodeBehind).DiagnosticIds().ShouldBe([id]);

	[Test]
	public void An_edited_storyboard_regenerates_on_the_next_run()
	{
		// What XAML Hot Reload does: the IDE swaps in the edited file and re-runs the generator. The edit must reach the
		// output rather than a cached result.
		var before = View("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" />""");
		var after = View("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Top)" To="40" />""");
		var (driver, compilation) = XamlGeneratorHarness.Create(before, CodeBehind);

		driver = driver.RunGenerators(compilation);
		driver.GetRunResult().AllGeneratedText().ShouldContain("global::Vysara.UIElement.OpacityProperty");

		driver = XamlGeneratorHarness.Edit(driver, after).RunGenerators(compilation);
		var code = driver.GetRunResult().AllGeneratedText();
		code.ShouldContain(".TargetProperty = new global::Vysara.Animation.PropertyPath(global::Vysara.Controls.Canvas.TopProperty);");
		code.ShouldContain(".To = 40d;");
		code.ShouldNotContain("OpacityProperty");
	}
}
