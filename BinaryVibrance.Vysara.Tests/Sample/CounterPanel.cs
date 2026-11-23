using BinaryVibrance.Vysara.Controls;

namespace BinaryVibrance.Vysara.Tests.Sample;

/// <summary>
/// The hand-authored half of <c>Sample/CounterPanel.xaml</c>, a view <c>NestingView.xaml</c> nests in its XAML. It names
/// the base the generated half derives from too, since the generator reading the nesting view's XAML sees only this
/// half; and it counts its rebuilds.
/// </summary>
internal sealed partial class CounterPanel : ContentControl
{
	/// <summary>How many times <see cref="OnComponentRebuilt"/> has run.</summary>
	public int Rebuilds;

	public CounterPanel() => InitializeComponent();

	partial void OnComponentRebuilt() => Rebuilds++;
}
