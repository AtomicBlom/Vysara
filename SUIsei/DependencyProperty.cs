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

	/// <summary>Register a normal dependency property on <typeparamref name="TOwner"/>.</summary>
	public static DependencyProperty<TOwner, TValue> Register<TOwner, TValue>(string name, PropertyMetadata<TOwner, TValue> metadata)
		where TOwner : DependencyObject =>
		new(name, DependencyPropertyKind.Normal, metadata);

	/// <summary>Register an attached dependency property declared by <typeparamref name="TOwner"/> (e.g. <c>Grid.Row</c>),
	/// settable on any <see cref="DependencyObject"/> target.</summary>
	public static DependencyProperty<TOwner, TValue> RegisterAttached<TOwner, TValue>(string name, PropertyMetadata<TOwner, TValue> metadata)
		where TOwner : DependencyObject =>
		new(name, DependencyPropertyKind.Attached, metadata);

	// Non-generic seams the (non-generic) effective-value store calls; the generic subclass casts and dispatches
	// the typed delegates — so there is no reflection anywhere on the set path.
	internal abstract object? DefaultValueBoxed { get; }
	internal abstract void RaiseChanged(DependencyObject owner, object? oldValue, object? newValue);
	internal abstract object? CoerceBoxed(DependencyObject owner, object? value);

	public override string ToString() => $"{Name}#{GlobalIndex}";
}

/// <summary>The concrete, typed dependency property.</summary>
public sealed class DependencyProperty<TOwner, TValue> : DependencyProperty where TOwner : DependencyObject
{
	internal DependencyProperty(string name, DependencyPropertyKind kind, PropertyMetadata<TOwner, TValue> metadata)
		: base(name, kind, metadata.Invalidates) => Metadata = metadata;

	public PropertyMetadata<TOwner, TValue> Metadata { get; }

	/// <summary>The typed default — boxed only when crossing the non-generic store seam, not on the generic fast path.</summary>
	public TValue DefaultValue => Metadata.DefaultValue;

	internal override object? DefaultValueBoxed => Metadata.DefaultValue;

	internal override void RaiseChanged(DependencyObject owner, object? oldValue, object? newValue) =>
		Metadata.PropertyChanged?.Invoke((TOwner)owner, (TValue)oldValue!, (TValue)newValue!);

	internal override object? CoerceBoxed(DependencyObject owner, object? value) =>
		Metadata.Coerce is { } coerce ? coerce((TOwner)owner, (TValue)value!) : value;
}
