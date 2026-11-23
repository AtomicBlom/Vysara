namespace BinaryVibrance.Vysara;

/// <summary>How an <see cref="Controls.Image"/> scales its source into its layout bounds (WinUI <c>Stretch</c>).</summary>
public enum Stretch
{
	/// <summary>Draw at the source's natural pixel size (in DIPs), top-left, no scaling.</summary>
	None,

	/// <summary>Stretch to fill the bounds exactly, ignoring aspect ratio.</summary>
	Fill,

	/// <summary>Scale to fit within the bounds preserving aspect ratio (letterboxed), centred.</summary>
	Uniform,

	/// <summary>Scale to cover the bounds preserving aspect ratio (overflow clipped), centred.</summary>
	UniformToFill,
}
