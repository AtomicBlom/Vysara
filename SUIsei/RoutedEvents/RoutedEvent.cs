using System.Threading;

namespace SUIsei;

/// <summary>
/// A routed-event identity: a dense <see cref="Id"/> assigned at registration, a diagnostic
/// <see cref="Name"/>, and the <see cref="Routing"/> strategy. Registered once in a static field initializer
/// (deterministic, AOT-safe — no assembly scanning), the same self-registration shape as
/// <see cref="DependencyProperty"/>. The dense id keys the per-element handler store with no string lookup.
/// </summary>
public sealed class RoutedEvent
{
	private static int _next;

	private RoutedEvent(ushort id, string name, RoutingStrategy routing)
	{
		Id = id;
		Name = name;
		Routing = routing;
	}

	/// <summary>Dense slot id (1-based; 0 is reserved for "none"). Keys the handler store.</summary>
	public ushort Id { get; }

	/// <summary>Diagnostics only — never a lookup key.</summary>
	public string Name { get; }

	/// <summary>Tunnel / bubble / direct.</summary>
	public RoutingStrategy Routing { get; }

	/// <summary>Register a routed event, assigning the next dense <see cref="Id"/>.</summary>
	public static RoutedEvent Register(string name, RoutingStrategy routing) =>
		new((ushort)Interlocked.Increment(ref _next), name, routing);

	public override string ToString() => Name;
}
