namespace BinaryVibrance.Vysara;

/// <summary>A width/height pair in device-independent pixels (DIPs). Either component may be
/// <see cref="float.PositiveInfinity"/> during measure (an unconstrained axis). Mirrors WinUI's <c>Size</c>.</summary>
public readonly record struct Size(float Width, float Height)
{
	public static Size Zero => new(0f, 0f);
	public static Size Infinite => new(float.PositiveInfinity, float.PositiveInfinity);
	public override string ToString() => $"{Width:0.###}x{Height:0.###}";
}
