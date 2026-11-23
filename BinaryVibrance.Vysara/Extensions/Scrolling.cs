using BinaryVibrance.Vysara.Controls;

namespace BinaryVibrance.Vysara.Extensions;

/// <summary>
/// Scrolling settings WinUI has no property for, set on a <see cref="ScrollViewer"/> in XAML as
/// <c>vysara:Scrolling.WheelScrollAmount="64"</c>.
/// </summary>
public sealed class Scrolling : DependencyObject
{
	private Scrolling()
	{
	}

	/// <summary>How far, in DIPs, one mouse-wheel notch or one Up/Down arrow key press scrolls. 48 by default.</summary>
	public static readonly DependencyProperty<Scrolling, float> WheelScrollAmountProperty =
		DependencyProperty.RegisterAttached<Scrolling, float>("WheelScrollAmount",
			new PropertyMetadata<Scrolling, float> { DefaultValue = 48f });

	public static void SetWheelScrollAmount(ScrollViewer scrollViewer, float value) => scrollViewer.SetValue(WheelScrollAmountProperty, value);
	public static float GetWheelScrollAmount(ScrollViewer scrollViewer) => scrollViewer.GetValue(WheelScrollAmountProperty);
}
