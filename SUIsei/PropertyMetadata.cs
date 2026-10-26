using SUIsei.Animation;

namespace SUIsei;

/// <summary>
/// The typed metadata for a <see cref="DependencyProperty{TOwner, TValue}"/>: default value, a typed
/// change callback (old, new — no boxing on the typed path), an optional coerce, what it invalidates, and how it
/// interpolates when animated.
/// Callbacks are <c>static</c> by convention (captured once at registration — no per-instance delegate, no
/// reflection).
/// </summary>
public sealed class PropertyMetadata<TOwner, TValue> where TOwner : DependencyObject
{
	public TValue DefaultValue { get; init; } = default!;
	public Action<TOwner, TValue, TValue>? PropertyChanged { get; init; }
	public Func<TOwner, TValue, TValue>? Coerce { get; init; }
	public InvalidationFlags Invalidates { get; init; }

	/// <summary>
	/// How the property interpolates when animated; a property with one is animatable. Left unset, a property of a
	/// type in <see cref="Lerps"/> (float, double, <see cref="System.Numerics.Vector2"/>,
	/// <see cref="System.Numerics.Vector3"/>, <see cref="Point"/>, <see cref="HdrColor"/>) uses the standard one,
	/// and any other type is not animatable.
	/// </summary>
	public Lerp<TValue>? Lerp { get; init; }
}
