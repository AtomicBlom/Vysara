using SUIsei.Composition;

namespace SUIsei;

/// <summary>
/// A <see cref="GlyphRunContent"/> shaped: its positioned glyphs in EM, line by line, and each line's width, kept on the
/// content as the work derived from it (<see cref="VisualContent.Derived"/>) and refilled in place. It records what it
/// was shaped from, the text, font, features and shaper, so a content change that leaves those alone (a resize, a new
/// brush, a new alignment) keeps it.
/// </summary>
internal sealed class ShapedGlyphRun
{
	/// <summary>The positioned glyphs of every line in order, whitespace left out, as <see cref="ITextShaper.Shape"/>
	/// produced them: each line's pen starts at 0, and each glyph's cluster is its character's index in the whole text.</summary>
	public List<ShapedGlyph> Glyphs { get; } = [];

	/// <summary>The lines, split where <see cref="TextLines"/> breaks them: at least one, empty for empty text.</summary>
	public List<ShapedLine> Lines { get; } = [];

	/// <summary>The widest line's advance width in EM.</summary>
	public float Width { get; private set; }

	/// <summary>The shaper the glyphs came from, null before the first shaping.</summary>
	public ITextShaper? Shaper => _shaper;

	private string? _text;
	private FontAtlas? _font;
	private FontFeatureSet? _features;
	private ITextShaper? _shaper;

	/// <summary>Whether this is the shaping of <paramref name="text"/> against <paramref name="font"/>'s current shaper with
	/// <paramref name="features"/>.</summary>
	public bool IsShapingOf(string text, FontAtlas font, FontFeatureSet features) =>
		ReferenceEquals(_font, font) && ReferenceEquals(_shaper, font.Shaper) && Equals(_features, features) && string.Equals(_text, text);

	/// <summary>Shape <paramref name="text"/> again into the retained glyph and line lists, one line at a time.</summary>
	public void Reshape(string text, FontAtlas font, FontFeatureSet features)
	{
		Glyphs.Clear();
		Lines.Clear();
		var shaper = font.Shaper;
		var width = 0f;
		var span = text.AsSpan();
		for (var start = 0; start >= 0;)
		{
			var line = span.Slice(start, TextLines.Next(span, start, out var next));
			var first = Glyphs.Count;
			var advance = shaper.Shape(line, font, features, Glyphs);
			if (start > 0)
			{
				for (var i = first; i < Glyphs.Count; i++)
					Glyphs[i] = Glyphs[i] with { Cluster = Glyphs[i].Cluster + start };
			}
			// Trailing whitespace hangs past where the line is placed, so only the rest is aligned.
			var trimmed = line.TrimEnd();
			var placed = trimmed.Length == line.Length ? advance : shaper.Measure(trimmed, font, features);
			Lines.Add(new ShapedLine(first, Glyphs.Count - first, advance, placed));
			width = MathF.Max(width, advance);
			start = next;
		}
		Width = width;
		_text = text;
		_font = font;
		_features = features;
		_shaper = shaper;
	}
}

/// <summary>
/// One line of a <see cref="ShapedGlyphRun"/>: <paramref name="GlyphCount"/> glyphs from <paramref name="FirstGlyph"/>, its
/// advance width in EM, and the width alignment places, in EM: the advance less any whitespace at the line's end.
/// </summary>
internal readonly record struct ShapedLine(int FirstGlyph, int GlyphCount, float WidthEm, float PlacedWidthEm);
