namespace Vysara;

/// <summary>Per-edge spacing in DIPs — used for <c>Margin</c>, <c>Padding</c>, and border thickness (WinUI <c>Thickness</c>).</summary>
public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
	public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }
	public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
	public static Thickness Zero => new(0f);

	/// <summary>Total horizontal inset (<see cref="Left"/> + <see cref="Right"/>).</summary>
	public float Horizontal => Left + Right;

	/// <summary>Total vertical inset (<see cref="Top"/> + <see cref="Bottom"/>).</summary>
	public float Vertical => Top + Bottom;

	public override string ToString() => $"({Left:0.###},{Top:0.###},{Right:0.###},{Bottom:0.###})";
}
