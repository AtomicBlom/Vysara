namespace SUIsei.Drawing;

/// <summary>
/// A glyph atlas page: the decoded RGBA8 MTSDF texels <see cref="FontAtlas.Image"/> exposes. SUIsei decodes the atlas
/// PNG itself (no engine dependency); as a <see cref="TextureSource"/> the page reaches the host through the slot table
/// of every draw list whose glyph runs sample it. The texels are signed distances, never sRGB.
/// </summary>
public sealed class AtlasImage(int width, int height, byte[] pixels) : TextureSource
{
	public override int Width { get; } = width;
	public override int Height { get; } = height;
	public override ReadOnlyMemory<byte> Pixels { get; } = pixels;
}
