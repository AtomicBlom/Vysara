using System.Numerics;

namespace SUIsei;

/// <summary>
/// A snapshot of a pointer at the moment of a routed pointer event (PlanningUI.md §D5.3) — the WinUI
/// <c>PointerPoint</c>, trimmed to what MU2 needs. <see cref="Position"/> is in surface pixels.
/// </summary>
public readonly record struct PointerPoint(Vector2 Position, PointerButtons Buttons, float Pressure);
