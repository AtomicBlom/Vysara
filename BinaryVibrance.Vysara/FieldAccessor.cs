namespace BinaryVibrance.Vysara;

/// <summary>The field of <typeparamref name="TOwner"/> that holds a field-backed property's value
/// (<see cref="PropertyMetadata{TOwner, TValue}.Field"/>), by reference.</summary>
internal delegate ref TValue FieldAccessor<in TOwner, TValue>(TOwner owner);
