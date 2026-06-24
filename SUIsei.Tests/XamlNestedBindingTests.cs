using SUIsei.Tests.Generated;

namespace SUIsei.Tests;

/// <summary>
/// Deep <c>{x:Bind}</c> proof (paths more than two segments, e.g. <c>ViewModel.Demo.Bloom.Intensity</c>): the
/// generated bindings push the initial value, track the leaf's INPC, re-subscribe + refresh when an intermediate
/// object is swapped, and never throw when any level along the chain is null (each reference-typed level is read
/// null-conditionally).
/// </summary>
public sealed class XamlNestedBindingTests
{
	[Test]
	public void Deep_OneWay_bind_pushes_the_initial_value()
	{
		var view = new NestedBindingView();

		view.Deep.Text.ShouldBe("0.50");   // ViewModel.Demo.Bloom.Intensity (four segments)
		view.Mid.Text.ShouldBe("Demo A");  // ViewModel.Demo.Title (three segments)
	}

	[Test]
	public void Leaf_change_deep_in_the_chain_updates_the_target()
	{
		var view = new NestedBindingView();

		view.ViewModel.Demo.Bloom!.Intensity = "0.90";

		view.Deep.Text.ShouldBe("0.90");
	}

	[Test]
	public void Swapping_an_intermediate_resubscribes_and_refreshes()
	{
		var view = new NestedBindingView();

		var fresh = new NestedBindingViewModel.BloomModel { Intensity = "0.10" };
		view.ViewModel.Demo.Bloom = fresh;
		view.Deep.Text.ShouldBe("0.10");   // the intermediate swap pushed the new sub-object's value

		fresh.Intensity = "0.20";
		view.Deep.Text.ShouldBe("0.20");   // ...and the newly-attached object is now tracked too
	}

	[Test]
	public void A_null_intermediate_falls_back_without_throwing()
	{
		var view = new NestedBindingView();

		Should.NotThrow(() => view.ViewModel.Demo.Bloom = null);

		view.Deep.Text.ShouldBe(string.Empty); // null anywhere up the chain coalesces, never NREs
	}

	[Test]
	public void Deep_bind_reads_and_subscribes_null_conditionally_at_every_level()
	{
		const string xaml = """
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="SUIsei.Tests.DeepView">
			    <TextBlock x:Name="Label" Text="{x:Bind Vm.A.B.Leaf, Mode=OneWay}" />
			</StackPanel>
			""";

		const string codeBehind = """
			using System.ComponentModel;
			namespace SUIsei.Tests
			{
			    partial class DeepView { public Root Vm { get; } = new(); }
			    class Root : INotifyPropertyChanged { public Mid A { get; set; } = new(); public event PropertyChangedEventHandler? PropertyChanged; }
			    class Mid : INotifyPropertyChanged { public Leafy B { get; set; } = new(); public event PropertyChangedEventHandler? PropertyChanged; }
			    class Leafy : INotifyPropertyChanged { public string Leaf { get; set; } = ""; public event PropertyChangedEventHandler? PropertyChanged; }
			}
			""";

		var result = XamlGeneratorHarness.Run(xaml, codeBehind);
		var text = result.AllGeneratedText();

		result.DiagnosticIds().ShouldNotContain("SUISEI008"); // path resolves at full depth
		text.ShouldContain("_root.Vm?.A?.B?.Leaf");           // every reference level read null-conditionally
		text.ShouldContain("Track(_root.Vm?.A)");             // owner of B is subscribed (null-safe)
		text.ShouldContain("Track(_root.Vm?.A?.B)");          // owner of the leaf is subscribed (null-safe)
		text.ShouldContain("case \"A\":");                    // intermediates rewire on change
		text.ShouldContain("case \"B\":");
		XamlGeneratorHarness.CompileErrors(xaml, codeBehind).ShouldBeEmpty(); // and it all compiles
	}
}
