namespace SUIsei;

/// <summary>
/// A <see cref="GlyphRunContent"/> shaped: its positioned glyphs in EM and its advance width, kept on the content as the
/// work derived from it (<see cref="VisualContent.Derived"/>) and refilled in place. It records what it was shaped from,
/// the text, font, features and shaper, so a content change that leaves those alone (a resize, a new brush) keeps it.
/// </summary>
internal sealed class ShapedGlyphRun
{
	/// <summary>The positioned glyphs, whitespace left out, as <see cref="ITextShaper.Shape"/> produced them.</summary>
	public List<ShapedGlyph> Glyphs { get; } = [];

	/// <summary>The run's total advance width in EM.</summary>
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

	/// <summary>Shape <paramref name="text"/> again into the retained glyph list.</summary>
	public void Reshape(string text, FontAtlas font, FontFeatureSet features)
	{
		Glyphs.Clear();
		var shaper = font.Shaper;
		Width = shaper.Shape(text, font, features, Glyphs);
		_text = text;
		_font = font;
		_features = features;
		_shaper = shaper;
	}
}
