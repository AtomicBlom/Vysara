using System.Numerics;
using SUIsei.Input;

namespace SUIsei.RoutedEvents;

/// <summary>
/// Payload for the pointer routed events: the <see cref="PointerPoint"/> and the originating
/// <see cref="Device"/>. Pooled by the input pipeline.
/// </summary>
public sealed class PointerRoutedEventArgs : RoutedEventArgs
{
	/// <summary>The pointer snapshot (position in surface pixels, buttons, pressure).</summary>
	public PointerPoint Pointer { get; internal set; }

	/// <summary>Mouse / pen / touch.</summary>
	public PointerDeviceType Device { get; internal set; }

	/// <summary>Scroll-wheel delta for a <see cref="UIElement.PointerWheelChanged"/> event (x horizontal,
	/// y vertical); zero otherwise.</summary>
	public Vector2 Wheel { get; internal set; }
}
