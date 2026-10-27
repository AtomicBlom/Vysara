using SUIsei.Text.Atlas;

namespace SUIsei.Text;

/// <summary>
/// The public entry point for native MTSDF font-atlas generation. Usable at build time (the
/// <c>loom-msdf</c> tool, the <c>RegisterFonts</c> MSBuild target) and at runtime (bake a font straight into a live
/// <see cref="FontAtlas"/>). Generation is the multi-threaded software path; output is a drop-in for the
/// <see cref="FontAtlas"/> format. GPU generation is a future addition behind the same facade.
/// </summary>
public static class MsdfBaker
{
	/// <summary>Bake a parsed fontset, resolving font file paths via <paramref name="resolveFont"/>.</summary>
	public static BakeResult Bake(Fontset fontset, Func<string, byte[]> resolveFont) =>
		AtlasBaker.Bake(fontset, resolveFont);

	/// <summary>Bake a parsed fontset, resolving relative font paths against <paramref name="baseDirectory"/>.</summary>
	public static BakeResult Bake(Fontset fontset, string baseDirectory) =>
		AtlasBaker.Bake(fontset, path => File.ReadAllBytes(ResolvePath(baseDirectory, path)));

	/// <summary>Read and bake a <c>*.fontset.json</c>; font paths resolve relative to the fontset file.</summary>
	public static BakeResult BakeFontsetFile(string fontsetPath)
	{
		var fontset = Fontset.Parse(File.ReadAllText(fontsetPath));
		var baseDir = Path.GetDirectoryName(Path.GetFullPath(fontsetPath)) ?? ".";
		return Bake(fontset, baseDir);
	}

	/// <summary>Bake a single in-memory font over a charset — the runtime / bake-on-miss path.</summary>
	public static BakeResult BakeFont(byte[] fontBytes, string name, BakeOptions? options = null)
	{
		var o = options ?? new BakeOptions();
		var fontset = new Fontset
		{
			Name = name,
			Fonts = [new FontEntry("<memory>", o.Charset, IsFallback: false, Weight: null, Script: null)],
			PxRange = o.PxRange,
			GlyphSize = o.GlyphSize,
			AtlasPageSize = o.AtlasPageSize,
			GlyphIdKeyed = o.GlyphIdKeyed,
		};

		return AtlasBaker.Bake(fontset, _ => fontBytes);
	}

	/// <summary>Bake a fontset and write <c>&lt;name&gt;.0.png</c> + <c>&lt;name&gt;.font.json</c> into <paramref name="outputDir"/>.</summary>
	public static BakeResult BakeFontsetToFiles(string fontsetPath, string outputDir)
	{
		var result = BakeFontsetFile(fontsetPath);
		WriteFiles(result, outputDir);
		return result;
	}

	/// <summary>Write a bake result's page + JSON to disk using the engine's naming (<c>name.0.png</c> / <c>name.font.json</c>).</summary>
	public static void WriteFiles(BakeResult result, string outputDir)
	{
		Directory.CreateDirectory(outputDir);
		File.WriteAllBytes(Path.Combine(outputDir, $"{result.Name}.0.png"), result.PngPage);
		File.WriteAllText(Path.Combine(outputDir, $"{result.Name}.font.json"), result.FontJson);
	}

	/// <summary>Encode top-down RGBA8 pixels as a PNG using the dependency-free encoder the atlas page uses.</summary>
	public static byte[] EncodePng(int width, int height, ReadOnlySpan<byte> rgba) => PngWriter.Encode(width, height, rgba);

	private static string ResolvePath(string baseDirectory, string path) =>
		Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
}

/// <summary>Parameters for baking a single font (the charset tokens and MTSDF dimensions).</summary>
public sealed class BakeOptions
{
	/// <summary>Charset range tokens (block names / <c>U+</c> ranges / "ascii"). Empty ⇒ printable ASCII.</summary>
	public IReadOnlyList<string> Charset { get; init; } = [];
	public int PxRange { get; init; } = Fontset.DefaultPxRange;
	public int GlyphSize { get; init; } = Fontset.DefaultGlyphSize;
	public int AtlasPageSize { get; init; } = Fontset.DefaultAtlasPageSize;

	/// <summary>Key the atlas by font glyph id (for a shaper) instead of by code point. Default false.</summary>
	public bool GlyphIdKeyed { get; init; }
}
