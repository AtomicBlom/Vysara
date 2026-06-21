using System.Numerics;

namespace SUIsei;

/// <summary>
/// One device-neutral pointer event the host queues into a <see cref="UIHost"/> between frames (PlanningUI.md §D5.2).
/// WinUI-shaped so mouse, pen and touch share one struct; the host (Sandbox/Silk.NET) translates raw input into
/// this, keeping graphics/windowing types out of <c>SUIsei</c>. <see cref="Position"/> is in surface pixels
/// (the same space as the composited <c>DrawListData</c> / the framebuffer); the host hit-tests and routes it in <see cref="UIHost.Tick"/>.
/// </summary>
/// <param name="PointerId">Distinguishes simultaneous pointers (multi-touch); mouse is a single stable id.</param>
/// <param name="Device">Mouse / pen / touch (MU2 drives mouse only).</param>
/// <param name="Position">Pointer position in surface pixels.</param>
/// <param name="Update">The button edge this event carries, or <see cref="PointerUpdateKind.None"/> for a move.</param>
/// <param name="Buttons">The buttons held after this event.</param>
/// <param name="Pressure">Normalised pressure (0..1); 1 for mouse/unsupported devices.</param>
/// <param name="Wheel">Scroll-wheel delta (x = horizontal, y = vertical); zero for a non-wheel event.</param>
/// <param name="TimestampMicros">Host timestamp in microseconds (for future multi-tap/gesture timing).</param>
public readonly record struct PointerInput(
	uint PointerId,
	PointerDeviceType Device,
	Vector2 Position,
	PointerUpdateKind Update,
	PointerButtons Buttons,
	float Pressure,
	Vector2 Wheel,
	ulong TimestampMicros)
{
	/// <summary>A pointer move (no button edge) at <paramref name="position"/> with <paramref name="buttons"/> held.</summary>
	public static PointerInput Move(Vector2 position, PointerButtons buttons = PointerButtons.None,
		uint pointerId = 0, PointerDeviceType device = PointerDeviceType.Mouse) =>
		new(pointerId, device, position, PointerUpdateKind.None, buttons, 1f, Vector2.Zero, 0);

	/// <summary>A button-press edge at <paramref name="position"/>.</summary>
	public static PointerInput Press(Vector2 position, PointerUpdateKind kind = PointerUpdateKind.LeftButtonPressed,
		PointerButtons buttons = PointerButtons.Left, uint pointerId = 0, PointerDeviceType device = PointerDeviceType.Mouse) =>
		new(pointerId, device, position, kind, buttons, 1f, Vector2.Zero, 0);

	/// <summary>A button-release edge at <paramref name="position"/>.</summary>
	public static PointerInput Release(Vector2 position, PointerUpdateKind kind = PointerUpdateKind.LeftButtonReleased,
		PointerButtons buttons = PointerButtons.None, uint pointerId = 0, PointerDeviceType device = PointerDeviceType.Mouse) =>
		new(pointerId, device, position, kind, buttons, 1f, Vector2.Zero, 0);
}
