using System.Numerics;

namespace SUIsei;

/// <summary>A solid (optionally rounded) rectangle with an optional border — the Border/Panel background primitive.</summary>
public sealed record RectContent(Vector2 Size, HdrColor? Fill, float CornerRadius = 0f, HdrColor? BorderBrush = null, float BorderThickness = 0f) : VisualContent;
