namespace BinaryVibrance.Vysara.Input;

/// <summary>
/// What changed in a <see cref="PointerInput"/> — the WinUI <c>PointerUpdateKind</c>. A plain
/// move carries <see cref="None"/>; a button edge carries the specific pressed/released kind that the input pipeline
/// turns into <c>PointerPressed</c>/<c>PointerReleased</c> routed events (and, on a matching pair, a synthesized
/// <c>Click</c>).
/// </summary>
public enum PointerUpdateKind : byte
{
	None,
	LeftButtonPressed,
	LeftButtonReleased,
	RightButtonPressed,
	RightButtonReleased,
	MiddleButtonPressed,
	MiddleButtonReleased,
}
