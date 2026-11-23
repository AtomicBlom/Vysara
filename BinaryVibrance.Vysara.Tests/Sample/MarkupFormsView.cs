using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests.Sample;

/// <summary>
/// The hand-authored half of the <c>Sample/MarkupFormsView.xaml</c> code-behind. BinaryVibrance.Vysara.SourceGen emits the other half
/// — the typed <c>x:Name</c> fields, the <c>Root</c>, resources, storyboards and <c>InitializeComponent()</c>.
/// </summary>
internal partial class MarkupFormsView
{
}

/// <summary>Static members the view reaches with <c>{x:Static local:SampleStrings.…}</c>.</summary>
internal static class SampleStrings
{
	public const string CardTitle = "Card";
	public static readonly SolidColorBrush Accent = new(HdrColor.FromRgb(10, 20, 30));
}

/// <summary>A control from a <c>using:</c> XML namespace (<c>using:BinaryVibrance.Vysara.Tests.Sample</c>).</summary>
internal sealed class SampleCaption : ContentControl
{
}
