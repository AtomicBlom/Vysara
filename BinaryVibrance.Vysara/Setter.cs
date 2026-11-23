namespace BinaryVibrance.Vysara;

/// <summary>
/// A single property assignment within a <see cref="Style"/>. Writes the Style rung of
/// the DP precedence ladder, so Local values and animations still win over a Style-sourced value.
/// </summary>
public sealed class Setter
{
	/// <summary>The dependency property to set.</summary>
	public required DependencyProperty Property { get; init; }

	/// <summary>The boxed value to write to the Style rung.</summary>
	public required object? Value { get; init; }

	internal void Apply(DependencyObject target) => target.SetStyleValue(Property, Value);

	internal void Validate(DependencyObject target) => Property.ValidateBoxed(target, Value);

	internal void Clear(DependencyObject target) => target.ClearStyleValue(Property);
}
