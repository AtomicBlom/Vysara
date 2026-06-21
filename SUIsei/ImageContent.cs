using System.Numerics;

using SUIsei.Drawing;

namespace SUIsei;

/// <summary>A tinted image drawn into <paramref name="Dest"/> (element-local pixels) from <paramref name="Source"/>
/// — the <see cref="Controls.Image"/> primitive the compositor emits as a <see cref="UIPrimKind.Image"/> command.</summary>
public sealed record ImageContent(ImageSource Source, Rect Dest) : VisualContent;
