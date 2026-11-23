using Vysara.Animation;

namespace Vysara;

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
	/// Checks a value where it enters the property, and throws <see cref="ArgumentException"/> (or a subclass, such as
	/// <see cref="ArgumentOutOfRangeException"/>) on one the property refuses, and nothing else: callers that test a value
	/// against it catch that alone. It checks a value set on the object, local or from a style, when it is set, and an
	/// animation's own values (its start and end, its keyframes) when it begins. What an animation produces between them, an
	/// easing's overshoot included, is not checked, so a running animation never throws; a property whose value must stay in
	/// range clamps where it is used. A value that comes from another property, inherited or template-bound, is not checked
	/// either: that property checks it where it is set. Unlike <see cref="Coerce"/>, it never changes a value.
	/// </summary>
	public Action<TOwner, TValue>? Validate { get; init; }
	public InvalidationFlags Invalidates { get; init; }
	/// <summary>
	/// Makes the property its owner's member of an inherited value, as WinUI's text properties are: its value passes down
	/// to the nearest elements below that hold a member of the same value, and below a local, style or animated value it
	/// takes the one passed down to it. Internal, as WinUI gives apps no inherited properties of their own.
	/// </summary>
	internal InheritedProperty<TValue>? Inherits { get; init; }

	/// <summary>
	/// How the property interpolates when animated; a property with one is animatable. Left unset, a property of a
	/// type in <see cref="Lerps"/> (float, double, <see cref="System.Numerics.Vector2"/>,
	/// <see cref="System.Numerics.Vector3"/>, <see cref="Point"/>, <see cref="HdrColor"/>) uses the standard one,
	/// and any other type is not animatable.
	/// </summary>
	public Lerp<TValue>? Lerp { get; init; }

	/// <summary>
	/// The owner's field that holds the property's value, as WinUI's core keeps <c>Width</c> in a field. Reading the
	/// property reads the field, and a local value lives there, so an object on which only code or markup set the property
	/// holds no slot for it in the store; a style, inherited or animated value makes one as for any property, and the field
	/// then follows the resolved value. The field starts at <see cref="DefaultValue"/>. Internal, for Vysara's own layout
	/// properties, which layout reads on every pass; such a property has no <see cref="Coerce"/> or
	/// <see cref="DefaultValueFor"/>, and is not attached.
	/// </summary>
	internal FieldAccessor<TOwner, TValue>? Field { get; init; }
}
