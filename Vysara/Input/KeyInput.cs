namespace Vysara.Input;

/// <summary>
/// One device-neutral keyboard event the host queues into <see cref="UIHost"/> between frames. The host
/// (Sandbox/Silk.NET) translates raw platform key events into this, keeping windowing types out of <c>Vysara</c>.
/// </summary>
/// <param name="Key">The virtual key that was pressed or released.</param>
/// <param name="Down">True for key-down, false for key-up.</param>
/// <param name="Repeat">True if this is an auto-repeat event (key held down).</param>
/// <param name="Modifiers">Modifier keys held at the time of the event.</param>
/// <param name="TimestampMicros">Host timestamp in microseconds.</param>
public readonly record struct KeyInput(
	VirtualKey Key,
	bool Down,
	bool Repeat,
	ModifierKeys Modifiers,
	ulong TimestampMicros);
