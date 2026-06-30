namespace SUIsei;

/// <summary>
/// The typed metadata for a <see cref="DependencyProperty{TOwner, TValue}"/>: default value, a typed
/// change callback (old, new — no boxing on the typed path), an optional coerce, and what it invalidates.
/// Callbacks are <c>static</c> by convention (captured once at registration — no per-instance delegate, no
/// reflection).
/// </summary>
public sealed class PropertyMetadata<TOwner, TValue> where TOwner : DependencyObject
{
	public TValue DefaultValue { get; init; } = default!;
	public Action<TOwner, TValue, TValue>? PropertyChanged { get; init; }
	public Func<TOwner, TValue, TValue>? Coerce { get; init; }
	public InvalidationFlags Invalidates { get; init; }
}
