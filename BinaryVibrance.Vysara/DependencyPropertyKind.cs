namespace BinaryVibrance.Vysara;

/// <summary>Whether a property is declared on its owner (<see cref="Normal"/>) or attached to arbitrary targets
/// (<see cref="Attached"/>, e.g. <c>Grid.Row</c>). Both share the one dense index space.</summary>
public enum DependencyPropertyKind { Normal, Attached }
