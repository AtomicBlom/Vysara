namespace SUIsei;

/// <summary>
/// The kind of device that produced a <see cref="PointerInput"/> — WinUI-shaped and device-neutral so no host
/// (Silk.NET) type leaks into <c>SUIsei</c>. Only <see cref="Mouse"/> is currently driven; pen and
/// touch use the same struct.
/// </summary>
public enum PointerDeviceType : byte { Mouse, Pen, Touch }
