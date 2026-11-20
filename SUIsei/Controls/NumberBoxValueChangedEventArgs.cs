namespace SUIsei.Controls;

/// <summary>A <see cref="NumberBox"/>'s value changed, as WinUI's <c>NumberBoxValueChangedEventArgs</c>.</summary>
public sealed class NumberBoxValueChangedEventArgs(double oldValue, double newValue) : EventArgs
{
	/// <summary>The value before, NaN for none.</summary>
	public double OldValue { get; } = oldValue;

	/// <summary>The value now, NaN for none.</summary>
	public double NewValue { get; } = newValue;
}
