namespace SUIsei.Tests.Generated;

/// <summary>
/// Code-behind for <c>BindingView.xaml</c> — the generator proof. The generator emits the partial half (typed
/// <c>Label</c>/<c>Action</c> fields, the tree, the <c>Action.Click += OnAction</c> wiring, and the
/// <c>{x:Bind ViewModel.Caption, Mode=OneWay}</c> glue); this half supplies the bind root and the handler.
/// </summary>
internal sealed partial class BindingView
{
	public BindingViewModel ViewModel { get; } = new();

	public int ActionCount;

	public BindingView() => InitializeComponent();

	private void OnAction(object sender, RoutedEventArgs e) => ActionCount++;
}
