using System.Numerics;
using SUIsei.Input;

namespace SUIsei.RoutedEvents;

/// <summary>
/// A snapshot of a pointer at the moment of a routed pointer event — the WinUI
/// <c>PointerPoint</c>, trimmed to what pointer routing needs. <see cref="Position"/> is in surface pixels.
/// </summary>
public readonly record struct PointerPoint(Vector2 Position, PointerButtons Buttons, float Pressure);
