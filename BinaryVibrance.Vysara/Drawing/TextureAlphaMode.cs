namespace BinaryVibrance.Vysara.Drawing;

/// <summary>
/// How a <see cref="TextureSource"/>'s colour channels relate to its alpha once its texels are decoded to linear light
/// (<see cref="TextureColorSpace"/>). A host samples the texel (decoding and filtering as its texture format does) and,
/// for a <see cref="Straight"/> source, multiplies the sampled colour by the sampled alpha before using it, as every
/// draw-list colour is premultiplied.
/// </summary>
public enum TextureAlphaMode : byte
{
	/// <summary>The decoded colour is already multiplied by alpha: sampled texels are used as they are.</summary>
	Premultiplied,

	/// <summary>The decoded colour is independent of alpha, as in a PNG: the sampled colour is multiplied by the sampled
	/// alpha after the decode and the filtering.</summary>
	Straight,
}
