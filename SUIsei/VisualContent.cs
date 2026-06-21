namespace SUIsei;

/// <summary>What a <see cref="SpriteVisual"/> draws. Phase B ships the rectangle family (fill + border); glyph-run
/// and image content arrive with MSDF text + images (MU1+). A small record hierarchy so the compositor switches
/// on the concrete type with no reflection.</summary>
public abstract record VisualContent;
