namespace Vysara;

/// <summary>
/// What becomes of a line still wider than its text block after any wrapping, set by
/// <see cref="Controls.TextBlock.TextTrimmingProperty"/>: WinUI's <c>TextTrimming</c>, with the values Vysara draws.
/// </summary>
public enum TextTrimming
{
	/// <summary>The line runs on past the width; the default.</summary>
	None = 0,

	/// <summary>The line is cut at a character, and an ellipsis ends it, inside the width.</summary>
	CharacterEllipsis = 1,
}
