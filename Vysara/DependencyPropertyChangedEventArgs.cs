namespace Vysara;

/// <summary>The data for a dependency-property change event such as <see cref="Controls.Control.IsEnabledChanged"/>:
/// the property and its old and new values, as in WinUI.</summary>
public sealed class DependencyPropertyChangedEventArgs(DependencyProperty property, object? oldValue, object? newValue) : EventArgs
{
	public DependencyProperty Property { get; } = property;
	public object? OldValue { get; } = oldValue;
	public object? NewValue { get; } = newValue;
}

/// <summary>Handles a dependency-property change event.</summary>
public delegate void DependencyPropertyChangedEventHandler(object sender, DependencyPropertyChangedEventArgs e);
