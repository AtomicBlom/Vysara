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
	private TextFit _fit;

	/// <summary>Whether this is the shaping of <paramref name="text"/> against <paramref name="font"/>'s current shaper with
	/// <paramref name="features"/>, fitted as <paramref name="fit"/> says.</summary>
	public bool IsShapingOf(string text, FontAtlas font, FontFeatureSet features, TextFit fit = default) =>
		ReferenceEquals(_font, font) && ReferenceEquals(_shaper, font.Shaper) && Equals(_features, features) && _fit == fit &&
		string.Equals(_text, text);

	/// <summary>Shape <paramref name="text"/> again into the retained glyph and line lists, one line at a time: a line per
	/// line break, each broken again at spaces and cut short with an ellipsis as <paramref name="fit"/> says.</summary>
	public void Reshape(string text, FontAtlas font, FontFeatureSet features, TextFit fit = default)
	{
		Glyphs.Clear();
		Lines.Clear();
		var shaper = font.Shaper;
		var width = 0f;
		var span = text.AsSpan();
		for (var start = 0; start >= 0;)
		{
			var length = TextLines.Next(span, start, out var next);
			if (fit.Wraps)
			{
				// Greedy: each line takes words while what it holds, its trailing spaces left out, keeps inside the width
				var end = start + length;
				var from = start;
				var breakAt = -1;   // where the line holding the words so far may end: after a run of spaces
				for (var i = start; i < end;)
				{
					while (i < end && span[i] != ' ')
						i++;
					if (breakAt > from && shaper.Measure(span[from..i], font, features) > fit.WidthEm)
					{
						width = MathF.Max(width, AddLine(span[from..breakAt], from));
						from = breakAt;
					}
					while (i < end && span[i] == ' ')
						i++;
					breakAt = i;
				}
				width = MathF.Max(width, AddLine(span[from..end], from));
			}
			else
				width = MathF.Max(width, AddLine(span.Slice(start, length), start));
			start = next;
		}
		Width = width;
		_text = text;
		_font = font;
		_features = features;
		_shaper = shaper;
		_fit = fit;

		// One line, shaped from where it starts in the text; cut to the width first if fit says so. Its advance.
		float AddLine(ReadOnlySpan<char> line, int start)
		{
			if (fit.Trims && shaper.Measure(line.TrimEnd(), font, features) > fit.WidthEm)
				return AddTrimmed(line.TrimEnd(), start);
			var first = Glyphs.Count;
			var advance = shaper.Shape(line, font, features, Glyphs);
			Offset(first, start);
			// Trailing whitespace hangs past where the line is placed, so only the rest is aligned.
			var trimmed = line.TrimEnd();
			var placed = trimmed.Length == line.Length ? advance : shaper.Measure(trimmed, font, features);
			Lines.Add(new ShapedLine(first, Glyphs.Count - first, advance, placed));
			return advance;
		}

		// As much of the line as fits with an ellipsis after it (found by halving), then the ellipsis; the font's own if
		// it has one, else three full stops
		float AddTrimmed(ReadOnlySpan<char> line, int start)
		{
			var ellipsis = font.TryGetGlyph('…', out _) ? "…" : "...";
			int low = 0, high = line.Length;
			while (low < high)
			{
				var mid = (low + high + 1) / 2;
				if (shaper.Measure(string.Concat(line[..mid].TrimEnd(), ellipsis), font, features) <= fit.WidthEm)
					low = mid;
				else
					high = mid - 1;
			}
			var cut = string.Concat(line[..low].TrimEnd(), ellipsis);
			var first = Glyphs.Count;
			var advance = shaper.Shape(cut, font, features, Glyphs);
			Offset(first, start);
			// The ellipsis stands for what was cut: its glyphs point at the last character kept
			for (var i = first; i < Glyphs.Count; i++)
				if (Glyphs[i].Cluster >= start + low)
					Glyphs[i] = Glyphs[i] with { Cluster = start + Math.Max(0, low - 1) };
			Lines.Add(new ShapedLine(first, Glyphs.Count - first, advance, advance));
			return advance;
		}

		void Offset(int first, int start)
		{
			if (start == 0)
				return;
			for (var i = first; i < Glyphs.Count; i++)
				Glyphs[i] = Glyphs[i] with { Cluster = Glyphs[i].Cluster + start };
		}
	}
}

/// <summary>
/// How a run's lines are fitted to a width: broken at spaces to keep inside it, cut short with an ellipsis, or neither
/// (the default, in which the width means nothing).
/// </summary>
/// <param name="WidthEm">The width, in EM: the text block's, over its font size.</param>
/// <param name="Wraps">Whether lines break at spaces to keep inside it.</param>
/// <param name="Trims">Whether a line still wider is cut short with an ellipsis.</param>
internal readonly record struct TextFit(float WidthEm, bool Wraps, bool Trims)
{
	/// <summary>The fit a text block asks for at <paramref name="width"/> (DIPs, infinite when unconstrained).</summary>
	public static TextFit For(float width, float fontSize, TextWrapping wrapping, TextTrimming trimming) =>
		float.IsFinite(width) && fontSize > 0f && (wrapping == TextWrapping.Wrap || trimming != TextTrimming.None)
			? new TextFit(width / fontSize, wrapping == TextWrapping.Wrap, trimming != TextTrimming.None)
			: default;
}

/// <summary>
/// One line of a <see cref="ShapedGlyphRun"/>: <paramref name="GlyphCount"/> glyphs from <paramref name="FirstGlyph"/>, its
/// advance width in EM, and the width alignment places, in EM: the advance less any whitespace at the line's end.
/// </summary>
internal readonly record struct ShapedLine(int FirstGlyph, int GlyphCount, float WidthEm, float PlacedWidthEm);
