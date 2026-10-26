using System.Numerics;
using SUIsei.Input;

namespace SUIsei.RoutedEvents;

/// <summary>
/// A snapshot of a pointer at the moment of a routed pointer event — the WinUI
/// <c>PointerPoint</c>, trimmed to what pointer routing needs. On <see cref="PointerRoutedEventArgs.Pointer"/>,
/// <see cref="Position"/> is in surface pixels; <see cref="PointerRoutedEventArgs.GetCurrentPoint"/> gives it in an
/// element's DIPs.
/// </summary>
public readonly record struct PointerPoint(Vector2 Position, PointerButtons Buttons, float Pressure);
