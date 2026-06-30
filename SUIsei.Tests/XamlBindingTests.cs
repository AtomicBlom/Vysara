using SUIsei.Tests.Generated;

namespace SUIsei.Tests;

/// <summary>
/// x:Bind + event generator proof: <c>BindingView.xaml</c> compiles to a typed
/// tree plus a <c>BindingView_Bindings</c> helper. A OneWay bind pushes the initial value and tracks
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/>; an event bind wires the handler.
/// </summary>
public sealed class XamlBindingTests
{
	[Test]
	public void OneWay_bind_pushes_the_initial_value_and_tracks_changes()
	{
		var view = new BindingView();

		view.Label.Text.ShouldBe("start");        // initial OneWay push at Initialize

		view.ViewModel.Caption = "changed";
		view.Label.Text.ShouldBe("changed");       // INPC PropertyChanged → Update_0
	}

	[Test]
	public void Event_binding_invokes_the_handler_on_click()
	{
		var view = new BindingView();

		view.Action.PerformClick();

		view.ActionCount.ShouldBe(1);
	}
}
