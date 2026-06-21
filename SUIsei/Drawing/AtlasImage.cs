namespace SUIsei.Drawing;

/// <summary>
/// Decoded RGBA8 pixels from a font atlas — the neutral image data FontAtlas exposes for the engine glue to upload
/// as a GPU texture. SUIsei decodes the atlas PNG itself (no engine dependency); the engine side creates whatever
/// texture type it needs from these bytes.
/// </summary>
public sealed class AtlasImage(int width, int height, byte[] pixels)
{
	public int Width { get; } = width;
	public int Height { get; } = height;
	public ReadOnlyMemory<byte> Pixels { get; } = pixels;
}
