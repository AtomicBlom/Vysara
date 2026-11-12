namespace SUIsei.Composition;

/// <summary>
/// How each line of a <see cref="GlyphRunContent"/> sits across its width, with the members and values of XAML's
/// <c>TextAlignment</c>. Text is laid out left to right only, so <see cref="Start"/> is <see cref="Left"/> and
/// <see cref="End"/> is <see cref="Right"/>. Whitespace at the end of a line hangs past where the line is placed.
/// </summary>
public enum CompositionTextAlignment
{
	/// <summary>
	/// Each line is centred in the width. It is 0, as in XAML, so <c>default</c> is centred: a glyph
	/// run starts at <see cref="Left"/>, which it sets itself.
	/// </summary>
	Center = 0,

	/// <summary>Each line starts at the left edge; what a glyph run starts with.</summary>
	Left = 1,

	/// <summary>Each line starts at the edge the text starts from: the left, as text is laid out left to right.</summary>
	Start = 1,

	/// <summary>Each line ends at the right edge.</summary>
	Right = 2,

	/// <summary>Each line ends at the edge the text ends at: the right, as text is laid out left to right.</summary>
	End = 2,

	/// <summary>A line that wraps is stretched to the width, and one that ends at a line break or at the end of the text
	/// starts at the left edge. Runs do not wrap, so every line is placed as <see cref="Left"/> places it.</summary>
	Justify = 3,
}
