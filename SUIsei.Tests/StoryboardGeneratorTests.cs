namespace SUIsei.Tests;

/// <summary>
/// Generator coverage for <c>&lt;VisualStateManager.VisualStateGroups&gt;</c> + <c>&lt;Storyboard&gt;</c> markup
/// (PlanningUI.md §D5.7): the generator emits the VSM groups, their state storyboards, and each animation with the
/// declarative Target/TargetProperty the runtime resolves — no reflection. Asserted on the generated source text.
/// </summary>
public sealed class StoryboardGeneratorTests
{
	private const string StatesXaml = """
		<Border xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		        x:Class="T.StatesView" x:Name="Bg">
		  <VisualStateManager.VisualStateGroups>
		    <VisualStateGroup Name="CommonStates">
		      <VisualState Name="Normal" />
		      <VisualState Name="Highlighted">
		        <Storyboard>
		          <ColorAnimation Storyboard.TargetName="Bg" Storyboard.TargetProperty="Background"
		                          To="#5060A0" Duration="0:0:0.12" FillBehavior="HoldEnd" />
		        </Storyboard>
		      </VisualState>
		    </VisualStateGroup>
		  </VisualStateManager.VisualStateGroups>
		</Border>
		""";

	[Test]
	public void Visual_state_groups_and_storyboards_are_emitted()
	{
		var result = XamlGeneratorHarness.Run(StatesXaml);
		var code = result.AllGeneratedText();

		result.DiagnosticIds().ShouldBeEmpty();
		code.ShouldContain("new global::SUIsei.VisualStateGroup { Name = \"CommonStates\" }");
		code.ShouldContain("new global::SUIsei.VisualState { Name = \"Normal\", Storyboard = null }");
		code.ShouldContain("new global::SUIsei.ColorAnimation()");
		code.ShouldContain(".To = global::SUIsei.HdrColor.FromRgb(80, 96, 160);");
		code.ShouldContain(".Duration = global::System.TimeSpan.FromSeconds(0.12);");
		code.ShouldContain(".FillBehavior = global::SUIsei.FillBehavior.HoldEnd;");
		code.ShouldContain(".Target = Bg;");
		code.ShouldContain(".TargetProperty = \"Background\";");
		code.ShouldContain("global::SUIsei.VisualStateManager.SetVisualStateGroups(Bg,");
	}

	[Test]
	public void Unknown_animatable_channel_diagnoses()
	{
		var xaml = """
			<Border xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			        x:Class="T.BadView" x:Name="Bg">
			  <VisualStateManager.VisualStateGroups>
			    <VisualStateGroup Name="G">
			      <VisualState Name="S">
			        <Storyboard>
			          <DoubleAnimation Storyboard.TargetName="Bg" Storyboard.TargetProperty="Nonsense" To="1" Duration="0:0:1" />
			        </Storyboard>
			      </VisualState>
			    </VisualStateGroup>
			  </VisualStateManager.VisualStateGroups>
			</Border>
			""";
		XamlGeneratorHarness.Run(xaml).DiagnosticIds().ShouldContain("SUISEI016");
	}
}
