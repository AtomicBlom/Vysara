namespace Vysara;

/// <summary>
/// How each line of text sits across the width its text block is arranged at, set by
/// <see cref="Controls.TextBlock.TextAlignmentProperty"/>: WinUI's <c>TextAlignment</c>, with its values. Vysara lays text
/// out left to right only, so <see cref="Start"/> is <see cref="Left"/> and <see cref="End"/> is <see cref="Right"/>, the
/// values they share in WinUI. Whitespace at the end of a line hangs past where the line is placed, so a trailing space
/// does not move right-aligned or centred text.
/// </summary>
public enum TextAlignment
{
	/// <summary>Each line is centred in the width.</summary>
	Center = 0,

	/// <summary>Each line starts at the left edge; the default.</summary>
	Left = 1,

	/// <summary>Each line starts at the edge the text starts from: the left, as Vysara lays text out left to right.</summary>
	Start = 1,

	/// <summary>Each line ends at the right edge.</summary>
	Right = 2,

	/// <summary>Each line ends at the edge the text ends at: the right, as Vysara lays text out left to right.</summary>
	End = 2,

	/// <summary>
	/// As WinUI justifies text: a line that wraps is stretched to the width, and a line that ends at a line break or at the
	/// end of the text starts at the left edge. Vysara doesn't stretch wrapped lines yet, so justified text is placed as
	/// <see cref="Left"/> is.
	/// </summary>
	Justify = 3,
}
