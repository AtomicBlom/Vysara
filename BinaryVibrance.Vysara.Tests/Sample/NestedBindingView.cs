namespace BinaryVibrance.Vysara.Tests.Sample;

/// <summary>
/// Code-behind for <c>NestedBindingView.xaml</c> — the deep (more-than-two-segment) <c>{x:Bind}</c> proof. The
/// generator emits the typed tree plus a <c>NestedBindingView_Bindings</c> helper that subscribes to every
/// intermediate object up each path; this half just supplies the bind root.
/// </summary>
internal sealed partial class NestedBindingView
{
	public NestedBindingViewModel ViewModel { get; } = new();

	public NestedBindingView() => InitializeComponent();
}
