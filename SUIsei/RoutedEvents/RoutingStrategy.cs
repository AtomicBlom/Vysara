namespace SUIsei;

/// <summary>
/// How a <see cref="RoutedEvent"/> travels the element tree (PlanningUI.md §D5.3). <see cref="Tunnel"/> goes
/// root→target (WinUI <c>Preview*</c>), <see cref="Bubble"/> target→root, <see cref="Direct"/> fires only on the
/// target. MU2 exercises <see cref="Bubble"/> (+ <see cref="Direct"/> for enter/exit); the tunnel pass is wired but
/// dormant until paired tunnel events arrive in MU3.
/// </summary>
public enum RoutingStrategy : byte { Tunnel, Bubble, Direct }
