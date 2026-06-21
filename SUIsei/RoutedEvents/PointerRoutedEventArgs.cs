namespace SUIsei;

/// <summary>
/// Payload for the pointer routed events (PlanningUI.md §D5.3): the <see cref="PointerPoint"/> and the originating
/// <see cref="Device"/>. Pooled by the input pipeline. Key modifiers arrive with keyboard input in MU3.
/// </summary>
public sealed class PointerRoutedEventArgs : RoutedEventArgs
{
	/// <summary>The pointer snapshot (position in surface pixels, buttons, pressure).</summary>
	public PointerPoint Pointer { get; internal set; }

	/// <summary>Mouse / pen / touch.</summary>
	public PointerDeviceType Device { get; internal set; }
}
