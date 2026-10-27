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

	private protected DependencyProperty(string name, DependencyPropertyKind kind, InvalidationFlags invalidates, Type ownerType, Type valueType)
	{
		GlobalIndex = Interlocked.Increment(ref _next) - 1;
		Name = name;
		Kind = kind;
		Invalidates = invalidates;
		OwnerType = ownerType;
		ValueType = valueType;
		if (Diagnostics.Enabled)
			AddToRegistry(this);
	}

	/// <summary>Dense slot id assigned at registration (the store key — never a string/Type lookup).</summary>
	public int GlobalIndex { get; }

	/// <summary>The property name — diagnostics/dumps only, never a lookup key.</summary>
	public string Name { get; }

	/// <summary>Normal vs Attached.</summary>
	public DependencyPropertyKind Kind { get; }

	/// <summary>What a resolved-value change invalidates.</summary>
	public InvalidationFlags Invalidates { get; }

	/// <summary>The type that registered the property: the declaring control, or for an attached property the type
	/// that declares it (<c>Grid</c> for <c>Grid.Row</c>).</summary>
	internal Type OwnerType { get; }

	/// <summary>The type of the property's value.</summary>
	internal Type ValueType { get; }

	// Each property by GlobalIndex, and each owner type's properties in registration order: what diagnostics lists for
	// an object. Filled only while diagnostics are on, so a game that leaves them off keeps no table.
	private static readonly Lock RegistryLock = new();
	private static Dictionary<int, DependencyProperty>? _byIndex;
	private static Dictionary<Type, List<DependencyProperty>>? _byOwner;

	private static void AddToRegistry(DependencyProperty dp)
	{
		lock (RegistryLock)
		{
			(_byIndex ??= [])[dp.GlobalIndex] = dp;
			_byOwner ??= [];
			if (!_byOwner.TryGetValue(dp.OwnerType, out var list))
				_byOwner[dp.OwnerType] = list = [];
			list.Add(dp);
		}
	}

	/// <summary>The properties <paramref name="owner"/> registered, normal and attached, in registration order; empty
	/// unless diagnostics are on.</summary>
	internal static DependencyProperty[] RegisteredBy(Type owner)
	{
		lock (RegistryLock)
			return _byOwner is not null && _byOwner.TryGetValue(owner, out var list) ? [.. list] : [];
	}

	/// <summary>The property with <paramref name="globalIndex"/>, or null when diagnostics are off.</summary>
	internal static DependencyProperty? FromIndex(int globalIndex)
	{
		lock (RegistryLock)
			return _byIndex is not null && _byIndex.TryGetValue(globalIndex, out var dp) ? dp : null;
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

	/// <summary>The property's resolved value on <paramref name="target"/>, boxed — the diagnostics read, never the
	/// typed path.</summary>
	internal abstract object? GetBoxedValue(DependencyObject target);

	/// <summary>Whether <paramref name="value"/> can be stored in this property: an instance of its value type, or null
	/// where the value type admits null.</summary>
	internal abstract bool Accepts(object? value);

	public override string ToString() => $"{Name}#{GlobalIndex}";
}

/// <summary>The concrete, typed dependency property.</summary>
public sealed class DependencyProperty<TOwner, TValue> : DependencyProperty, IAnimationTarget<TValue> where TOwner : DependencyObject
{
	internal DependencyProperty(string name, DependencyPropertyKind kind, PropertyMetadata<TOwner, TValue> metadata)
		: base(name, kind, metadata.Invalidates, typeof(TOwner), typeof(TValue))
	{
		Metadata = metadata;
		Lerp = metadata.Lerp ?? Lerps.For<TValue>();
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

	internal override object? GetBoxedValue(DependencyObject target) => target.GetValue(this);

	internal override bool Accepts(object? value) => value is TValue || (value is null && default(TValue) is null);

	string IAnimationTarget.ValueTypeName => typeof(TValue).Name;

	TValue IAnimationTarget<TValue>.Read(DependencyObject target) => target.GetValue(this);

	void IAnimationTarget<TValue>.Write(DependencyObject target, TValue value) => target.SetAnimatedValue(this, value);

	void IAnimationTarget<TValue>.Release(DependencyObject target) => target.ClearAnimatedValue(this);
}
