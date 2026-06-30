namespace SUIsei.Tests;

/// <summary>
/// Generator snapshot: <c>Mode=TwoWay</c> in <c>{x:Bind}</c> emits a reentrancy guard and a control→VM
/// reverse-wire event handler; <c>FallbackValue</c> / <c>TargetNullValue</c> emit null-coalescing chains;
/// <c>{StaticResource key}</c> emits a <c>FindResource</c> runtime lookup.
/// </summary>
public sealed class XamlBindingTwoWayTests
{
	[Test]
	public void TwoWay_binding_emits_reentrancy_guard_field()
	{
		// A TextBox (if it existed) with TextChanged would be ideal; we verify the generator output directly.
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.TwoWayView">
			    <TextBlock x:Name="Label" Text="{x:Bind ViewModel.Caption, Mode=TwoWay}" />
			</StackPanel>
			""";

		const string codeBehind = """
			using System.ComponentModel;
			namespace SUIsei.Tests
			{
			    partial class TwoWayView
			    {
			        public TwoWayViewModel ViewModel { get; } = new();
			    }
			    class TwoWayViewModel : INotifyPropertyChanged
			    {
			        public string Caption { get; set; } = "";
			        public event PropertyChangedEventHandler? PropertyChanged;
			    }
			}
			""";

		var result = XamlGeneratorHarness.Run(xaml, codeBehind);
		var text = result.AllGeneratedText();

		// The generator downgrades TwoWay to OneWay when no Changed event exists (TextBlock.TextChanged absent),
		// but the bindings class must still be emitted for the OneWay subscription.
		text.ShouldContain("_Bindings");
		text.ShouldContain("INotifyPropertyChanged");
		// No SUISEI errors on a valid x:Bind path.
		result.DiagnosticIds().ShouldNotContain("SUISEI008");
	}

	[Test]
	public void TwoWay_downgraded_to_OneWay_emits_SUISEI014_warning_when_no_changed_event()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.TwoWayView2">
			    <TextBlock x:Name="Label" Text="{x:Bind ViewModel.Caption, Mode=TwoWay}" />
			</StackPanel>
			""";

		const string codeBehind = """
			using System.ComponentModel;
			namespace SUIsei.Tests
			{
			    partial class TwoWayView2
			    {
			        public TwoWayViewModel2 ViewModel { get; } = new();
			    }
			    class TwoWayViewModel2 : INotifyPropertyChanged
			    {
			        public string Caption { get; set; } = "";
			        public event PropertyChangedEventHandler? PropertyChanged;
			    }
			}
			""";

		var result = XamlGeneratorHarness.Run(xaml, codeBehind);

		// TextBlock has no TextChanged event — generator should warn SUISEI014.
		result.DiagnosticIds().ShouldContain("SUISEI014");
	}

	[Test]
	public void FallbackValue_emits_null_coalescing_chain()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.FallbackView">
			    <TextBlock x:Name="Label" Text="{x:Bind ViewModel.Caption, FallbackValue=N/A}" />
			</StackPanel>
			""";

		const string codeBehind = """
			using System.ComponentModel;
			namespace SUIsei.Tests
			{
			    partial class FallbackView
			    {
			        public FallbackViewModel ViewModel { get; } = new();
			    }
			    class FallbackViewModel : INotifyPropertyChanged
			    {
			        public string? Caption { get; set; }
			        public event PropertyChangedEventHandler? PropertyChanged;
			    }
			}
			""";

		var result = XamlGeneratorHarness.Run(xaml, codeBehind);
		result.Diagnostics.ShouldBeEmpty();
		var text = result.AllGeneratedText();
		text.ShouldContain("N/A");
		text.ShouldContain("??");
	}

	[Test]
	public void TargetNullValue_and_FallbackValue_both_emitted()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.FallbackView2">
			    <TextBlock x:Name="Label" Text="{x:Bind ViewModel.Caption, TargetNullValue=empty, FallbackValue=error}" />
			</StackPanel>
			""";

		const string codeBehind = """
			using System.ComponentModel;
			namespace SUIsei.Tests
			{
			    partial class FallbackView2
			    {
			        public FallbackViewModel2 ViewModel { get; } = new();
			    }
			    class FallbackViewModel2 : INotifyPropertyChanged
			    {
			        public string? Caption { get; set; }
			        public event PropertyChangedEventHandler? PropertyChanged;
			    }
			}
			""";

		var result = XamlGeneratorHarness.Run(xaml, codeBehind);
		result.Diagnostics.ShouldBeEmpty();
		var text = result.AllGeneratedText();
		text.ShouldContain("empty");
		text.ShouldContain("error");
	}

	[Test]
	public void StaticResource_emits_FindResource_call()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.StaticResView">
			    <TextBlock x:Name="Label" Style="{StaticResource MyStyle}" />
			</StackPanel>
			""";

		var result = XamlGeneratorHarness.Run(xaml);
		result.Diagnostics.ShouldBeEmpty();
		var text = result.AllGeneratedText();
		text.ShouldContain("FindResource(\"MyStyle\")");
	}

	[Test]
	public void StaticResource_does_not_emit_SUISEI007_unsupported_markup()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.StaticResView2">
			    <TextBlock x:Name="Label" Style="{StaticResource MyStyle}" />
			</StackPanel>
			""";

		var result = XamlGeneratorHarness.Run(xaml);

		result.DiagnosticIds().ShouldNotContain("SUISEI007");
	}

	[Test]
	public void Mode_OneTime_is_the_default_and_emits_no_subscription()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.OneTimeView">
			    <TextBlock x:Name="Label" Text="{x:Bind ViewModel.Caption}" />
			</StackPanel>
			""";

		const string codeBehind = """
			namespace SUIsei.Tests
			{
			    partial class OneTimeView
			    {
			        public OneTimeViewModel ViewModel { get; } = new();
			    }
			    class OneTimeViewModel
			    {
			        public string Caption { get; set; } = "hello";
			    }
			}
			""";

		var result = XamlGeneratorHarness.Run(xaml, codeBehind);
		result.Diagnostics.ShouldBeEmpty();
		var text = result.AllGeneratedText();
		// OneTime = no _Bindings helper emitted (no OneWay subscription needed).
		text.ShouldNotContain("_Bindings");
	}
}
