namespace Vysara;

/// <summary>
/// Whether a text block's lines break where they would run past its width, set by
/// <see cref="Controls.TextBlock.TextWrappingProperty"/>: WinUI's <c>TextWrapping</c>. A line breaks after the last space
/// that keeps it inside the width; a word wider than the width alone stays whole, on a line of its own, past it (unless
/// <see cref="TextTrimming"/> cuts it).
/// </summary>
public enum TextWrapping
{
	/// <summary>Lines break only at the text's line breaks; the default.</summary>
	NoWrap = 1,

	/// <summary>Lines also break at spaces, to keep inside the width.</summary>
	Wrap = 2,
}
