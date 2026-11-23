namespace Vysara.RoutedEvents;

/// <summary>
/// The base routed-event payload. <see cref="OriginalSource"/> is the element the event was
/// raised on; <see cref="Source"/> is the current element as the route advances; <see cref="Handled"/> short-circuits
/// the remaining route (a handler that registered <c>handledEventsToo</c> still runs). Mutable + reusable so
/// the input pipeline can pool one instance per dispatch (honouring the "0 GC in the render loop" rule).
/// </summary>
public class RoutedEventArgs
{
	/// <summary>The routed event being dispatched.</summary>
	public RoutedEvent? RoutedEvent { get; internal set; }

	/// <summary>The element the event originated on (constant across the route).</summary>
	public object? OriginalSource { get; internal set; }

	/// <summary>The element currently handling the event as the route advances.</summary>
	public object? Source { get; internal set; }

	/// <summary>Set by a handler to stop the event travelling further along the route.</summary>
	public bool Handled { get; set; }

	/// <summary>Reset to the start-of-dispatch state (for pooled reuse).</summary>
	internal void Reset(RoutedEvent evt, object originalSource)
	{
		RoutedEvent = evt;
		OriginalSource = originalSource;
		Source = originalSource;
		Handled = false;
	}
}
