using System.Numerics;

using SUIsei.Controls;

namespace SUIsei.Extensions;

/// <summary>
/// Test-only members of the extension XAML namespace (<c>https://github.com/AtomicBlom/SUIsei/extensions</c>, which maps
/// <c>SUIsei.Extensions</c>), so <c>Sample/MarkupFormsView.xaml</c> can use the <c>suisei:</c> prefix before the runtime
/// declares any extension types. Their names are test-specific so they never collide with real ones.
/// </summary>
public sealed class TestGlow : DependencyObject
{
	/// <summary>An attached, animatable float, set as <c>suisei:TestGlow.Radius</c> and animated through the
	/// <c>(suisei:TestGlow.Radius)</c> property path.</summary>
	public static readonly DependencyProperty<TestGlow, float> RadiusProperty =
		DependencyProperty.RegisterAttached<TestGlow, float>("Radius", new PropertyMetadata<TestGlow, float>());

	public static void SetRadius(UIElement element, float value) => element.SetValue(RadiusProperty, value);
	public static float GetRadius(UIElement element) => element.GetValue(RadiusProperty);
}

/// <summary>A test element with properties of the structured value types the XAML converters parse.</summary>
public sealed class TestBadge : ContentControl
{
	public Point Anchor { get; set; }
	public Vector2 Scale { get; set; }
	public Vector3 Offset { get; set; }
	public HdrColor Tint { get; set; }
	public HdrColor? Halo { get; set; }
}
