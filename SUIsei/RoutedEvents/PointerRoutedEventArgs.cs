using System.Numerics;
using SUIsei.Input;

namespace SUIsei.RoutedEvents;

/// <summary>
/// Payload for the pointer routed events: the <see cref="PointerPoint"/> and the originating
/// <see cref="Device"/>. Pooled by the input pipeline.
/// </summary>
public sealed class PointerRoutedEventArgs : RoutedEventArgs
{
	/// <summary>The pointer snapshot (position in surface pixels, buttons, pressure).</summary>
	public PointerPoint Pointer { get; internal set; }

	/// <summary>Mouse / pen / touch.</summary>
	public PointerDeviceType Device { get; internal set; }

	/// <summary>The pointer the event is for: the one to pass to <see cref="UIElement.CapturePointer"/>.</summary>
	public uint PointerId { get; internal set; }

	/// <summary>Scroll-wheel delta for a <see cref="UIElement.PointerWheelChanged"/> event (x horizontal,
	/// y vertical); zero otherwise.</summary>
	public Vector2 Wheel { get; internal set; }

	/// <summary>
	/// The pointer with its position in <paramref name="relativeTo"/>'s local space (DIPs), or in its root's DIP space
	/// when null, as WinUI's <c>GetCurrentPoint</c>. It maps through the same composed visual transforms hit-testing
	/// uses, so a control under a render transform, an animated translate or a scaled root reads the point it was hit
	/// at. Allocation-free.
	/// </summary>
	public PointerPoint GetCurrentPoint(UIElement? relativeTo)
	{
		var root = relativeTo?.AttachedRoot ?? (OriginalSource as UIElement)?.AttachedRoot;
		var toDip = root is not null && Matrix3x2.Invert(root.Visual.Transform, out var inverse) ? inverse : Matrix3x2.Identity;
		var dip = Vector2.Transform(Pointer.Position, toDip);
		if (relativeTo is null)
			return Pointer with { Position = dip };
		return Pointer with
		{
			Position = Matrix3x2.Invert(relativeTo.TransformToRoot(out _), out var toLocal)
				? Vector2.Transform(dip, toLocal)
				: new Vector2(float.NaN, float.NaN),
		};
	}
}
