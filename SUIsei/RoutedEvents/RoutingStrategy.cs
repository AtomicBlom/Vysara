namespace SUIsei;

/// <summary>
/// How a <see cref="RoutedEvent"/> travels the element tree. <see cref="Tunnel"/> goes
/// root→target (WinUI <c>Preview*</c>), <see cref="Bubble"/> target→root, <see cref="Direct"/> fires only on the
/// target.
/// </summary>
public enum RoutingStrategy : byte { Tunnel, Bubble, Direct }
