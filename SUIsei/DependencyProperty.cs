using SUIsei.Animation;

namespace SUIsei;

/// <summary>
/// A reflection-free dependency property — the WinUI semantics (typed metadata, change/coerce callbacks, value
/// precedence) with the <see cref="Type"/>-keyed, string-dispatched mechanism replaced by a dense integer slot
/// (<see cref="GlobalIndex"/>) and typed delegates. Abstract base; the concrete property is
/// always a <see cref="DependencyProperty{TOwner, TValue}"/>. Identity is by reference.
/// </summary>
public abstract class DependencyProperty
{
	// Dense, monotonically increasing slot ids assigned at registration — lets DependencyObject use a small
	// int-keyed store instead of a reference-hashed dictionary.
	private static int _next;

	private protected DependencyProperty(string name, DependencyPropertyKind kind, InvalidationFlags invalidates)
	{
		GlobalIndex = Interlocked.Increment(ref _next) - 1;
		Name = name;
		Kind = kind;
		Invalidates = invalidates;
	}

	/// <summary>Dense slot id assigned at registration (the store key — never a string/Type lookup).</summary>
	public int GlobalIndex { get; }

	/// <summary>The property name — diagnostics/dumps only, never a lookup key.</summary>
	public string Name { get; }

	/// <summary>Normal vs Attached.</summary>
	public DependencyPropertyKind Kind { get; }

	/// <summary>What a resolved-value change invalidates.</summary>
	public InvalidationFlags Invalidates { get; }

	// Field-backed properties (PropertyMetadata.Field) each take one bit of an object's record of the local values it
	// holds in fields, so there can be 64 of them.
	private static int _nextField;

	/// <summary>This property's bit in <see cref="DependencyObject"/>'s record of the local values it holds in fields, for a
	/// field-backed property (<see cref="PropertyMetadata{TOwner, TValue}.Field"/>); 0 for any other.</summary>
	internal ulong FieldBit { get; private protected init; }

	private protected static ulong NextFieldBit(string name)
	{
		var index = Interlocked.Increment(ref _nextField) - 1;
		return index < 64 ? 1UL << index
			: throw new InvalidOperationException($"{name} would be field-backed property {index + 1}; DependencyObject records 64.");
	}

	/// <summary>Whether an animation can drive this property: it has a
	/// <see cref="PropertyMetadata{TOwner, TValue}.Lerp"/>, declared or standard for its type.</summary>
	public abstract bool IsAnimatable { get; }

	/// <summary>Register a normal dependency property on <typeparamref name="TOwner"/>.</summary>
	public static DependencyProperty<TOwner, TValue> Register<TOwner, TValue>(string name, PropertyMetadata<TOwner, TValue> metadata)
		where TOwner : DependencyObject =>
		new(name, DependencyPropertyKind.Normal, metadata);

	/// <summary>Register an attached dependency property declared by <typeparamref name="TOwner"/> (e.g. <c>Grid.Row</c>),
	/// settable on any <see cref="DependencyObject"/> target.</summary>
	public static DependencyProperty<TOwner, TValue> RegisterAttached<TOwner, TValue>(string name, PropertyMetadata<TOwner, TValue> metadata)
		where TOwner : DependencyObject =>
		new(name, DependencyPropertyKind.Attached, metadata);

	// Seams for callers that hold only the non-generic property — a style setter's boxed value, clearing a rung. The
	// typed subclass unboxes once and writes the typed store, so there is no reflection on the set path.
	internal abstract void SetRungBoxed(DependencyObject target, DependencyObject.ValueRung rung, object? value);
	internal abstract void ClearRung(DependencyObject target, DependencyObject.ValueRung rung);

	/// <summary>The property's value on <paramref name="target"/> as a <see cref="DependencyObject"/>, or null when it
	/// holds none (or holds a value type) — how a property path steps from one object to the next.</summary>
	internal abstract DependencyObject? GetObjectValue(DependencyObject target);

	/// <summary>Whether <paramref name="target"/> can hold this property: any object for an attached property, else an
	/// instance of its owner type.</summary>
	internal abstract bool AppliesTo(DependencyObject target);

	public override string ToString() => $"{Name}#{GlobalIndex}";
}

/// <summary>The concrete, typed dependency property.</summary>
public sealed class DependencyProperty<TOwner, TValue> : DependencyProperty, IAnimationTarget<TValue> where TOwner : DependencyObject
{
	internal DependencyProperty(string name, DependencyPropertyKind kind, PropertyMetadata<TOwner, TValue> metadata)
		: base(name, kind, metadata.Invalidates)
	{
		Metadata = metadata;
		Lerp = metadata.Lerp ?? Lerps.For<TValue>();
		if (metadata.Field is not null)
		{
			// The field holds the resolved value as is, so nothing may change it on the way out, and it starts at one default.
			if (metadata.Coerce is not null || metadata.DefaultValueFor is not null || kind == DependencyPropertyKind.Attached)
				throw new ArgumentException($"{name} keeps its value in a field, so it can have no Coerce or DefaultValueFor and cannot be attached.", nameof(metadata));
			FieldBit = NextFieldBit(name);
		}
	}

	public PropertyMetadata<TOwner, TValue> Metadata { get; }

	/// <summary>The typed default.</summary>
	public TValue DefaultValue => Metadata.DefaultValue;

	/// <summary>How the property interpolates when animated: the metadata's, else the standard one for its type, else
	/// null (not animatable).</summary>
	public Lerp<TValue>? Lerp { get; }

	public override bool IsAnimatable => Lerp is not null;

	internal override void SetRungBoxed(DependencyObject target, DependencyObject.ValueRung rung, object? value) =>
		target.SetRung(this, rung, (TValue)value!, hasValue: true);

	internal override void ClearRung(DependencyObject target, DependencyObject.ValueRung rung) =>
		target.SetRung(this, rung, default!, hasValue: false);

	internal override DependencyObject? GetObjectValue(DependencyObject target) =>
		typeof(TValue).IsValueType ? null : target.GetValue(this) as DependencyObject;

	internal override bool AppliesTo(DependencyObject target) => Kind == DependencyPropertyKind.Attached || target is TOwner;

	string IAnimationTarget.ValueTypeName => typeof(TValue).Name;

	TValue IAnimationTarget<TValue>.Read(DependencyObject target) => target.GetValue(this);

	void IAnimationTarget<TValue>.Write(DependencyObject target, TValue value) => target.SetAnimatedValue(this, value);

	void IAnimationTarget<TValue>.Release(DependencyObject target) => target.ClearAnimatedValue(this);

	bool IAnimationTarget<TValue>.Validates => Metadata.Validate is not null;

	void IAnimationTarget<TValue>.Validate(DependencyObject target, TValue value) => Metadata.Validate?.Invoke((TOwner)target, value);
}
