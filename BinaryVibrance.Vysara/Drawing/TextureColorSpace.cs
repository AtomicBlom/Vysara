namespace BinaryVibrance.Vysara.Drawing;

/// <summary>
/// How a <see cref="TextureSource"/>'s RGB bytes encode light. A host binds an <see cref="Srgb"/> source through an sRGB
/// texture format, so the hardware decodes each texel to linear light before filtering, and a <see cref="Linear"/> one
/// through a plain (UNORM) format, read as stored. Alpha is never encoded.
/// </summary>
public enum TextureColorSpace : byte
{
	/// <summary>The bytes are linear values (data such as a glyph atlas's distances, or linear light): read as stored.</summary>
	Linear,

	/// <summary>The bytes are sRGB-encoded colour, as in an ordinary image file: decoded to linear light per texel,
	/// before filtering.</summary>
	Srgb,
}
