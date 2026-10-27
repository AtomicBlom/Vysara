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

	/// <summary>
	/// The default for one object, where it depends on the object's type, as a WinUI class overrides the default of a
	/// property it inherits (a <c>Rectangle</c>'s <c>Stretch</c> is <c>Fill</c>, a <c>Path</c>'s <c>None</c>). It takes
	/// <see cref="DefaultValue"/>'s place when set; a style or a local value still overrides it. Not for attached
	/// properties, whose owner is not the object holding them.
	/// </summary>
	public Func<TOwner, TValue>? DefaultValueFor { get; init; }

	public Action<TOwner, TValue, TValue>? PropertyChanged { get; init; }
	public Func<TOwner, TValue, TValue>? Coerce { get; init; }
	/// <summary>
	/// Checks a value where it enters the property, and throws on one the property refuses: a local or style value when
	/// it is set, and an animation's own values (its start and end, its keyframes) when it begins. What an animation
	/// produces between them, an easing's overshoot included, is not checked, so a running animation never throws; a
	/// property whose value must stay in range clamps where it is used. Unlike <see cref="Coerce"/>, it never changes a
	/// value.
	/// </summary>
	public Action<TOwner, TValue>? Validate { get; init; }
	public InvalidationFlags Invalidates { get; init; }

	/// <summary>
	/// How the property interpolates when animated; a property with one is animatable. Left unset, a property of a
	/// type in <see cref="Lerps"/> (float, double, <see cref="System.Numerics.Vector2"/>,
	/// <see cref="System.Numerics.Vector3"/>, <see cref="Point"/>, <see cref="HdrColor"/>) uses the standard one,
	/// and any other type is not animatable.
	/// </summary>
	public Lerp<TValue>? Lerp { get; init; }
}
