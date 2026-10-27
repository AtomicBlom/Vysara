namespace SUIsei.Tests;

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
		<Border xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		        x:Class="T.StatesView" x:Name="Bg">
		  <VisualStateManager.VisualStateGroups>
		    <VisualStateGroup Name="CommonStates">
		      <VisualState Name="Normal" />
		      <VisualState Name="Highlighted">
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
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            x:Class="Demo.V">
		  <Canvas Width="200" Height="100">
		    <Border x:Name="Box" Width="10" Height="10" Background="#FF0000" />
		  </Canvas>
		  <TextBlock x:Name="Label" Text="Hi" />
		  <ContentControl x:Name="Host">
		    <VisualStateManager.VisualStateGroups>
		      <VisualStateGroup Name="G">
		        <VisualState Name="S">
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
		code.ShouldContain("new global::SUIsei.Animation.VisualStateGroup { Name = \"CommonStates\" }");
		code.ShouldContain("new global::SUIsei.Animation.VisualState { Name = \"Normal\", Storyboard = null }");
		code.ShouldContain("new global::SUIsei.Animation.ColorAnimation()");
		code.ShouldContain(".To = global::SUIsei.HdrColor.FromRgb(80, 96, 160);");
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(0.12);");
		code.ShouldContain(".FillBehavior = global::SUIsei.Animation.FillBehavior.HoldEnd;");
		code.ShouldContain(".Target = Bg;");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.Border.BackgroundProperty, global::SUIsei.Media.SolidColorBrush.ColorProperty);");
		code.ShouldContain("global::SUIsei.Animation.VisualStateManager.SetVisualStateGroups(Bg,");
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
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.UIElement.OpacityProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.Canvas.LeftProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.TextBlock.FontSizeProperty);");
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
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.Border.BackgroundProperty, global::SUIsei.Media.SolidColorBrush.ColorProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.TextBlock.ForegroundProperty, global::SUIsei.Media.SolidColorBrush.ColorProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.UIElement.RenderTransformProperty, global::SUIsei.Media.CompositeTransform.TranslateXProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.UIElement.RenderTransformProperty, global::SUIsei.Media.TranslateTransform.YProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.Border.BackgroundProperty, global::SUIsei.Media.Brush.OpacityProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.UIElement.ClipProperty, global::SUIsei.Media.Geometry.TransformProperty, global::SUIsei.Media.TranslateTransform.XProperty);");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Background\" To=\"Blue\" />", "(Border.Background).(SolidColorBrush.Color)")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"(Border.Background)\" To=\"Blue\" />", "(Border.Background).(SolidColorBrush.Color)")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Label\" Storyboard.TargetProperty=\"Foreground\" To=\"Blue\" />", "(TextBlock.Foreground).(SolidColorBrush.Color)")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Host\" Storyboard.TargetProperty=\"Background\" To=\"Blue\" />", "(ContentControl.Background).(SolidColorBrush.Color)")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"RenderTransform.Translate.X\" To=\"5\" />", "(UIElement.RenderTransform).(TranslateTransform.X)")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"RenderTransform.Translate.Y\" To=\"5\" />", "(UIElement.RenderTransform).(TranslateTransform.Y)")]
	public void The_animation_shorthand_from_before_brushes_diagnoses_SUISEI023_with_the_WinUI_path(string animation, string suggestion)
	{
		var diagnostic = XamlGeneratorHarness.Run(View(animation), CodeBehind).Diagnostics.Single();
		diagnostic.Id.ShouldBe("SUISEI023");
		diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).ShouldEndWith($"write the WinUI path '{suggestion}'");
	}

	[Test]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"#0000FF\" />")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Background\" To=\"1\" />")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"(Grid.Row)\" To=\"1\" />")]
	public void A_property_the_animation_cannot_drive_diagnoses_SUISEI016(string animation) =>
		XamlGeneratorHarness.Run(View(animation), CodeBehind).DiagnosticIds().ShouldBe(["SUISEI016"]);

	[Test]
	[Arguments("Nonsense")]                                      // no such property
	[Arguments("(Nope.Opacity)")]                                // unknown owner type
	[Arguments("(TextBlock.FontSize)")]                          // a TextBlock property, on a Border
	[Arguments("Foreground")]                                    // a Border has no Foreground
	[Arguments("(UIElement.Opacity).(UIElement.Opacity)")]       // Opacity holds a float, not an object to step into
	[Arguments("(UIElement.Opacity")]                            // malformed
	[Arguments("Opacity.")]                                      // malformed
	public void An_unresolvable_path_diagnoses_SUISEI021(string path) =>
		XamlGeneratorHarness.Run(View($"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="{path}" To="1" />"""), CodeBehind)
			.DiagnosticIds().ShouldBe(["SUISEI021"]);

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

	// The default View with the extension namespace declared as `suisei:` and SUIsei.Controls as `using:`.
	private static string PrefixedView(string animations) => View(animations).Replace(
		"x:Class=\"Demo.V\"",
		"xmlns:suisei=\"https://github.com/AtomicBlom/SUIsei/extensions\" xmlns:ctl=\"using:SUIsei.Controls\" x:Class=\"Demo.V\"");

	[Test]
	public void Owner_prefixes_resolve_through_their_XML_namespace()
	{
		var xaml = PrefixedView("""
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(suisei:Glow.Radius)" To="3" />
			<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(ctl:Canvas.Top)" To="4" />
			""");
		var result = XamlGeneratorHarness.Run(xaml, ExtensionCodeBehind);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Extensions.Glow.RadiusProperty);");
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.Canvas.TopProperty);");
		XamlGeneratorHarness.CompileErrors(xaml, ExtensionCodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("(suisei:Nope.Radius)", "SUISEI022")]   // no such extension type
	[Arguments("(nope:Glow.Radius)", "SUISEI021")]     // an undeclared prefix
	[Arguments("(ctl:Glow.Radius)", "SUISEI021")]      // declared, but the type is not in that namespace
	public void An_unresolvable_owner_prefix_diagnoses(string path, string id) =>
		XamlGeneratorHarness.Run(PrefixedView($"""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="{path}" To="1" />"""), ExtensionCodeBehind)
			.DiagnosticIds().ShouldBe([id]);

	[Test]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\" Bogus=\"2\" />", "SUISEI004")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"lots\" />", "SUISEI012")]
	[Arguments("<ColorAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"(Border.Background).(SolidColorBrush.Color)\" From=\"Blurple\" />", "SUISEI012")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\" Duration=\"later\" />", "SUISEI012")]
	public void A_bad_animation_attribute_is_an_error(string animation, string id) =>
		XamlGeneratorHarness.Run(View(animation), CodeBehind).DiagnosticIds().ShouldBe([id]);

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
		code.ShouldContain("new global::SUIsei.Animation.DoubleAnimationUsingKeyFrames()");
		code.ShouldContain(".KeyTime = global::System.TimeSpan.FromSeconds(0.5);");
		code.ShouldContain(".IsDiscrete = true;");
		code.ShouldContain(".EasingFunction = new global::SUIsei.Animation.CubicEase { EasingMode = global::SUIsei.Animation.EasingMode.EaseIn };");
		code.ShouldContain(".EasingFunction = new global::SUIsei.Animation.BackEase { Amplitude = 0.5d, EasingMode = global::SUIsei.Animation.EasingMode.EaseInOut };");
		// With no Duration, a keyframe animation lasts until its last key.
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(2);");
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
	}

	[Test]
	[Arguments("<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\"><LinearDoubleKeyFrame KeyTime=\"soon\" Value=\"1\" /></DoubleAnimationUsingKeyFrames>", "SUISEI012")]
	[Arguments("<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\"><SplineDoubleKeyFrame KeyTime=\"0:0:1\" Value=\"1\" /></DoubleAnimationUsingKeyFrames>", "SUISEI020")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\"><DoubleAnimation.EasingFunction><BounceEase /></DoubleAnimation.EasingFunction></DoubleAnimation>", "SUISEI003")]
	[Arguments("<DoubleAnimation Storyboard.TargetName=\"Box\" Storyboard.TargetProperty=\"Opacity\" To=\"1\"><DoubleAnimation.EasingFunction><CubicEase EasingMode=\"Sideways\" /></DoubleAnimation.EasingFunction></DoubleAnimation>", "SUISEI012")]
	public void Bad_keyframes_and_easing_functions_diagnose(string animation, string id) =>
		XamlGeneratorHarness.Run(View(animation), CodeBehind).DiagnosticIds().ShouldBe([id]);

	[Test]
	public void An_edited_storyboard_regenerates_on_the_next_run()
	{
		// What XAML Hot Reload does: the IDE swaps in the edited file and re-runs the generator. The edit must reach the
		// output rather than a cached result.
		var before = View("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Opacity" To="0.5" />""");
		var after = View("""<DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="(Canvas.Top)" To="40" />""");
		var (driver, compilation) = XamlGeneratorHarness.Create(before, CodeBehind);

		driver = driver.RunGenerators(compilation);
		driver.GetRunResult().AllGeneratedText().ShouldContain("global::SUIsei.UIElement.OpacityProperty");

		driver = XamlGeneratorHarness.Edit(driver, after).RunGenerators(compilation);
		var code = driver.GetRunResult().AllGeneratedText();
		code.ShouldContain(".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.Controls.Canvas.TopProperty);");
		code.ShouldContain(".To = 40d;");
		code.ShouldNotContain("OpacityProperty");
	}
}
