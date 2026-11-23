using System.Text;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Text.Atlas;

/// <summary>
/// The output of a bake: the MTSDF page PNG and the metrics JSON in the <see cref="BinaryVibrance.Vysara.FontAtlas"/> format, plus
/// the page dimensions and any code points that no font in the set could cover (build-time warnings).
/// </summary>
public sealed class BakeResult
{
	public required string Name { get; init; }

	/// <summary>The atlas page encoded as a PNG (RGBA8 MTSDF), i.e. <c>&lt;name&gt;.0.png</c>.</summary>
	public required byte[] PngPage { get; init; }

	/// <summary>The metrics JSON text, i.e. <c>&lt;name&gt;.font.json</c>.</summary>
	public required string FontJson { get; init; }

	public required int Width { get; init; }
	public required int Height { get; init; }

	/// <summary>Code points requested by the charset that no font covered — surface these as build warnings.</summary>
	public required IReadOnlyList<int> MissingCodepoints { get; init; }

	/// <summary>Decode straight into a live <see cref="BinaryVibrance.Vysara.FontAtlas"/> (the runtime / test / bake-on-miss path).</summary>
	public FontAtlas ToFontAtlas()
	{
		using var json = new MemoryStream(Encoding.UTF8.GetBytes(FontJson));
		using var png = new MemoryStream(PngPage);
		return FontAtlas.Load(json, png);
	}

	/// <summary>The decoded page as the neutral <see cref="AtlasImage"/> (RGBA8) — handy for previewing without a FontAtlas.</summary>
	public AtlasImage ToAtlasImage() => ToFontAtlas().Image;
}
