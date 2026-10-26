using SUIsei.Controls;
using SUIsei.Media;

namespace SUIsei.Tests.Sample;

/// <summary>
/// The hand-authored half of the <c>Sample/GeometryView.xaml</c> code-behind. SUIsei.SourceGen emits the other half.
/// </summary>
internal partial class GeometryView
{
}

/// <summary>A test element with properties of the geometry value types, set from <c>Sample/GeometryView.xaml</c>.</summary>
internal sealed class GeometryHost : ContentControl
{
	public Geometry? Data { get; set; }
	public PointCollection Points { get; set; } = [];
	public DoubleCollection Dashes { get; set; } = [];
}
