using System.Globalization;
using Vysara.Text.Atlas;

namespace Vysara.Text.Tool;

/// <summary>Parsed <c>vysara-msdf</c> command-line options.</summary>
internal sealed class ToolOptions
{
	public string? FontsetPath { get; private set; }
	public string? FontPath { get; private set; }
	public string? Name { get; private set; }
	public string OutputDir { get; private set; } = ".";
	public IReadOnlyList<string> Charset { get; private set; } = [];
	public int PxRange { get; private set; } = Fontset.DefaultPxRange;
	public int GlyphSize { get; private set; } = Fontset.DefaultGlyphSize;
	public int PageSize { get; private set; } = Fontset.DefaultAtlasPageSize;
	public string? PreviewText { get; private set; }
	public float PreviewSize { get; private set; } = 48f;

	/// <summary>Debug: render the preview from the embedded committed atlas (FontAtlas.Default) instead of baking.</summary>
	public bool Builtin { get; private set; }

	/// <summary>Debug: print the baked page's reconstructed coverage as ASCII art (to inspect field artifacts).</summary>
	public bool DumpAscii { get; private set; }

	public static ToolOptions Parse(string[] args)
	{
		var o = new ToolOptions();
		for (var i = 0; i < args.Length; i++)
		{
			var a = args[i];
			switch (a)
			{
				case "-o" or "--output": o.OutputDir = Next(args, ref i); break;
				case "--font": o.FontPath = Next(args, ref i); break;
				case "--name": o.Name = Next(args, ref i); break;
				case "--charset": o.Charset = Next(args, ref i).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
				case "--glyph-size": o.GlyphSize = ParseInt(Next(args, ref i), a); break;
				case "--px-range": o.PxRange = ParseInt(Next(args, ref i), a); break;
				case "--page-size": o.PageSize = ParseInt(Next(args, ref i), a); break;
				case "--preview": o.PreviewText = Next(args, ref i); break;
				case "--preview-size": o.PreviewSize = ParseInt(Next(args, ref i), a); break;
				case "--builtin": o.Builtin = true; break;
				case "--dump-ascii": o.DumpAscii = true; break;
				case "--cpu": break; // the only path today; accepted for forward compatibility
				default:
					if (a.StartsWith('-'))
						throw new ArgumentException($"unknown option '{a}'");
					o.FontsetPath = a; // a positional argument is the fontset path
					break;
			}
		}

		return o;
	}

	private static string Next(string[] args, ref int i)
	{
		if (i + 1 >= args.Length)
			throw new ArgumentException($"option '{args[i]}' expects a value");
		return args[++i];
	}

	private static int ParseInt(string value, string option) =>
		int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
			? v
			: throw new ArgumentException($"option '{option}' expects an integer, got '{value}'");
}
