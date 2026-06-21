using System.Diagnostics;
using SUIsei.Text;
using SUIsei.Text.Atlas;
using SUIsei.Text.Tool;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
	PrintUsage();
	return args.Length == 0 ? 1 : 0;
}

try
{
	var options = ToolOptions.Parse(args);

	if (options.Builtin)
	{
		// Diagnostic: render the committed embedded atlas through the same preview path, to compare against a bake.
		var builtinAtlas = SUIsei.FontAtlas.Default;
		var text = options.PreviewText ?? "Hello, SUIsei MSDF 0123 {}";
		var (rgba, w, h) = PreviewRenderer.Render(builtinAtlas, text, options.PreviewSize);
		Directory.CreateDirectory(options.OutputDir);
		var path = Path.Combine(options.OutputDir, "builtin.preview.png");
		File.WriteAllBytes(path, MsdfBaker.EncodePng(w, h, rgba));
		Console.WriteLine($"Rendered committed atlas preview: {path}");
		return 0;
	}

	var sw = Stopwatch.StartNew();

	BakeResult result;
	if (options.FontsetPath is not null)
	{
		result = MsdfBaker.BakeFontsetFile(options.FontsetPath);
	}
	else if (options.FontPath is not null)
	{
		var bytes = File.ReadAllBytes(options.FontPath);
		var name = options.Name ?? Path.GetFileNameWithoutExtension(options.FontPath);
		result = MsdfBaker.BakeFont(bytes, name, new BakeOptions
		{
			Charset = options.Charset,
			PxRange = options.PxRange,
			GlyphSize = options.GlyphSize,
			AtlasPageSize = options.PageSize,
		});
	}
	else
	{
		Console.Error.WriteLine("Specify a *.fontset.json path or --font <path> --name <name>.");
		PrintUsage();
		return 1;
	}

	sw.Stop();
	MsdfBaker.WriteFiles(result, options.OutputDir);

	var glyphCount = CountGlyphs(result.FontJson);
	Console.WriteLine($"Baked '{result.Name}': {result.Width}×{result.Height} page, {glyphCount} glyphs, {sw.ElapsedMilliseconds} ms");
	Console.WriteLine($"  {Path.Combine(options.OutputDir, result.Name + ".0.png")}");
	Console.WriteLine($"  {Path.Combine(options.OutputDir, result.Name + ".font.json")}");

	if (result.MissingCodepoints.Count > 0)
	{
		var sample = string.Join(", ", result.MissingCodepoints.Take(16).Select(cp => $"U+{cp:X4}"));
		Console.WriteLine($"  warning: {result.MissingCodepoints.Count} requested code point(s) had no covering glyph: {sample}{(result.MissingCodepoints.Count > 16 ? ", …" : "")}");
	}

	if (options.DumpAscii)
	{
		var img = result.ToAtlasImage();
		var page = img.Pixels.Span;
		var cols = Math.Min(img.Width, 90);
		var rows = Math.Max(1, (int)(cols * (img.Height / (float)img.Width) * 0.5f));
		const string ramp = " .:-=+*#%@";
		for (var ry = 0; ry < rows; ry++)
		{
			var line = new char[cols];
			for (var rx = 0; rx < cols; rx++)
			{
				var sx = Math.Min(img.Width - 1, rx * img.Width / cols);
				var sy = Math.Min(img.Height - 1, ry * img.Height / rows);
				var o = (sy * img.Width + sx) * 4;
				var med = MathF.Max(MathF.Min(page[o], page[o + 1]), MathF.Min(MathF.Max(page[o], page[o + 1]), page[o + 2])) / 255f;
				var cov = Math.Clamp((med - 0.5f) * 6f + 0.5f, 0f, 1f);
				line[rx] = ramp[Math.Clamp((int)(cov * (ramp.Length - 1) + 0.5f), 0, ramp.Length - 1)];
			}

			Console.WriteLine(new string(line));
		}
	}

	if (options.PreviewText is not null)
	{
		var atlas = result.ToFontAtlas();
		var (rgba, w, h) = PreviewRenderer.Render(atlas, options.PreviewText, options.PreviewSize);
		var previewPath = Path.Combine(options.OutputDir, result.Name + ".preview.png");
		File.WriteAllBytes(previewPath, MsdfBaker.EncodePng(w, h, rgba));
		Console.WriteLine($"  preview: {previewPath}  ({options.PreviewText.Length} chars at {options.PreviewSize}px)");
	}

	return 0;
}
catch (Exception ex)
{
	Console.Error.WriteLine($"loom-msdf: {ex.Message}");
	return 1;
}

static int CountGlyphs(string json)
{
	using var doc = System.Text.Json.JsonDocument.Parse(json);
	return doc.RootElement.GetProperty("glyphs").GetArrayLength();
}

static void PrintUsage()
{
	Console.WriteLine(
		"""
		loom-msdf — native MTSDF font-atlas generator (SUIsei / Loom)

		Usage:
		  loom-msdf <fontset.json> [-o <dir>] [--preview "<text>"] [--preview-size <px>]
		  loom-msdf --font <path> --name <name> [options]

		Options:
		  -o, --output <dir>     Output directory (default: current directory)
		  --font <path>          Bake a single .ttf/.otf instead of a fontset
		  --name <name>          Output base name when baking a single font
		  --charset <tokens>     Comma-separated block names / U+ ranges (default: printable ASCII)
		  --glyph-size <px>      Baked EM size in texels (default: 32)
		  --px-range <px>        MTSDF distance range in texels (default: 4)
		  --page-size <px>       Max atlas page dimension (default: 1024)
		  --preview "<text>"     Also render a preview PNG of the baked atlas
		  --preview-size <px>    Preview font size (default: 48)
		  --cpu                  Use the software path (the only path today; accepted for forward-compat)
		  -h, --help             Show this help

		Outputs <name>.0.png (RGBA8 MTSDF page) and <name>.font.json (the FontAtlas layout).
		""");
}
