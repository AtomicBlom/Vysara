using System.Buffers;

namespace BinaryVibrance.Vysara;

/// <summary>
/// Where a text's lines break: at Unicode's mandatory breaks (UAX #14's BK, CR, LF and NL classes), so a line ends at a
/// line feed, a carriage return (with the line feed after it, if any, as one break), a vertical tab, a form feed, a next
/// line, a line separator or a paragraph separator. Text with none is one line; a break at the end leaves an empty line
/// after it.
/// </summary>
internal static class TextLines
{
	private static readonly SearchValues<char> Breaks = SearchValues.Create("\n\v\f\r\u0085\u2028\u2029");

	/// <summary>
	/// The length of the line starting at <paramref name="start"/>, without its break. <paramref name="next"/> is where
	/// the line after it starts, past the break, or -1 when it is the last line. Walk a text's lines with
	/// <c>for (var start = 0; start &gt;= 0; start = next)</c>.
	/// </summary>
	public static int Next(ReadOnlySpan<char> text, int start, out int next)
	{
		var length = text[start..].IndexOfAny(Breaks);
		if (length < 0)
		{
			next = -1;
			return text.Length - start;
		}
		var end = start + length;
		next = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
		return length;
	}
}
