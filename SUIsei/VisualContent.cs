namespace SUIsei;

/// <summary>What a <see cref="SpriteVisual"/> draws: the rectangle family (fill + border), MSDF glyph-run text,
/// and image content. A small record hierarchy so the compositor switches
/// on the concrete type with no reflection.</summary>
public abstract record VisualContent;
