using System.Runtime.CompilerServices;
using SUIsei.Gallery;
using SUIsei.Media.Imaging;

namespace SUIsei.Tests;

/// <summary>
/// Each gallery page drawn on its own, as <c>SUIsei.Gallery --capture-pages</c> draws it, and the whole gallery in its
/// default window, against their golden images in <c>Goldens/Gallery/</c>. The CPU rasterizer draws the same pixels every
/// run, but not to the bit on every machine (the C runtime's transcendental functions differ in their last bits between
/// platforms), so an image matches when at most 0.5% of its pixels differ by more than 4/255 in any channel, the bar Loom
/// holds its two backends to. Set <c>SUISEI_GOLDEN_UPDATE=1</c> to write the goldens instead. An image that fails leaves
/// what was drawn in the temp directory, named in the failure.
/// </summary>
[NotInParallel(TestIsolation.HotReload)]
public sealed class GalleryGoldenTests
{
	private const string SkipReason = "The gallery shapes its text with HarfBuzz, whose native library ships for Windows only (SUIsei.HarfBuzz).";
	private const string ShellGolden = "shell";
	private const string MenuGolden = "shell-menu";
	private const int Tolerance = 4;
	private const double MostDiffering = 0.005;

	public static IEnumerable<string> PageKeys() => GalleryPages.All.Select(static p => p.Key);

	[Test]
	[MethodDataSource(nameof(PageKeys))]
	public void A_gallery_page_draws_as_its_golden(string key)
	{
		Skip.Unless(OperatingSystem.IsWindows(), SkipReason);
		MatchesGolden(key, GalleryCapture.Page(GalleryPages.Find(key)));
	}

	/// <summary>The shell in the gallery's default 960x720 window: its menu bar, the list with the first page selected,
	/// and that page beside it.</summary>
	[Test]
	public void The_gallery_shell_draws_as_its_golden()
	{
		Skip.Unless(OperatingSystem.IsWindows(), SkipReason);
		MatchesGolden(ShellGolden, GalleryCapture.Shell(null, 960, 720, frames: GalleryCapture.SettleFrames));
	}

	/// <summary>The shell with its View menu open: a drop-down as big as its items, under the menu bar's View, over the
	/// page.</summary>
	[Test]
	public void The_gallery_shell_draws_its_open_menu_as_its_golden()
	{
		Skip.Unless(OperatingSystem.IsWindows(), SkipReason);
		MatchesGolden(MenuGolden, GalleryCapture.Shell(null, 960, 720, frames: GalleryCapture.SettleFrames, then: static shell => shell.ViewMenu.OpenMenu()));
	}

	/// <summary>A page draws nothing in its right margin. TextBlock doesn't wrap, so a line too long for the page area runs
	/// into the margin and off the page, and in the shell widens the whole page past the window; a measured size can't
	/// show it, since it is clamped to the space there was.</summary>
	[Test]
	[MethodDataSource(nameof(PageKeys))]
	public void A_gallery_page_fits_the_page_area(string key)
	{
		Skip.Unless(OperatingSystem.IsWindows(), SkipReason);
		var capture = GalleryCapture.Page(GalleryPages.Find(key));
		var background = GalleryTheme.PageBackground;
		byte[] expected = [(byte)MathF.Round(background.R * 255f), (byte)MathF.Round(background.G * 255f), (byte)MathF.Round(background.B * 255f), 255];
		// The pages are inset 28 from each side; the last 24 columns leave the cards' antialiased edges alone.
		for (var y = 0; y < capture.Height; y++)
		{
			for (var x = capture.Width - 24; x < capture.Width; x++)
			{
				var o = (y * capture.Width + x) * 4;
				if (!capture.Rgba.AsSpan(o, 4).SequenceEqual(expected))
					throw new ShouldAssertException($"The '{key}' page draws in its right margin, at ({x}, {y}).");
			}
		}
	}

	/// <summary>Every golden is a page's or the shell's: a page that is removed or renamed takes its golden with it.</summary>
	[Test]
	public void Every_gallery_golden_belongs_to_a_page()
	{
		var names = PageKeys().Append(ShellGolden).Append(MenuGolden).ToHashSet();
		var goldens = Directory.Exists(GoldensDirectory()) ? Directory.GetFiles(GoldensDirectory(), "*.png") : [];
		goldens.Select(Path.GetFileNameWithoutExtension).Where(name => !names.Contains(name!)).ShouldBeEmpty();
	}

	// Compare `capture` with the golden called `name`, or write it as that golden under SUISEI_GOLDEN_UPDATE=1.
	private static void MatchesGolden(string name, GalleryCapture capture)
	{
		DistinctColours(capture.Rgba).ShouldBeGreaterThan(200, "the gallery draws text and antialiased edges, so a near-blank frame means it drew nothing");

		var path = Path.Combine(GoldensDirectory(), $"{name}.png");
		if (Environment.GetEnvironmentVariable("SUISEI_GOLDEN_UPDATE") == "1")
		{
			Directory.CreateDirectory(GoldensDirectory());
			capture.Save(path);
			return;
		}
		File.Exists(path).ShouldBeTrue($"'{name}' has no golden at {path}: run with SUISEI_GOLDEN_UPDATE=1 to write it.");

		var golden = BitmapSource.FromBytes(File.ReadAllBytes(path));
		var actual = Path.Combine(Path.GetTempPath(), "suisei-gallery", $"{name}.actual.png");
		if ((golden.PixelWidth, golden.PixelHeight) != (capture.Width, capture.Height))
		{
			Keep(capture, actual);
			throw new ShouldAssertException($"'{name}' drew {capture.Width}x{capture.Height}; its golden is {golden.PixelWidth}x{golden.PixelHeight}. It drew {actual}.");
		}
		var (differing, worst) = Compare(golden.Pixels.Span, capture.Rgba);
		var fraction = differing / (double)(capture.Width * capture.Height);
		if (fraction > MostDiffering)
		{
			Keep(capture, actual);
			throw new ShouldAssertException(
				$"'{name}' differs from its golden in {fraction:P2} of its pixels (at most {MostDiffering:P1} may differ by more than {Tolerance}/255; the worst is {worst}/255). It drew {actual}.");
		}
	}

	// How many pixels differ by more than the tolerance in any channel, and the largest difference in any channel.
	private static (int Differing, int Worst) Compare(ReadOnlySpan<byte> golden, ReadOnlySpan<byte> actual)
	{
		var differing = 0;
		var worst = 0;
		for (var i = 0; i < actual.Length; i += 4)
		{
			var pixel = 0;
			for (var c = 0; c < 4; c++)
				pixel = Math.Max(pixel, Math.Abs(golden[i + c] - actual[i + c]));
			if (pixel > Tolerance)
				differing++;
			worst = Math.Max(worst, pixel);
		}
		return (differing, worst);
	}

	private static int DistinctColours(byte[] rgba)
	{
		var colours = new HashSet<int>();
		for (var i = 0; i < rgba.Length; i += 4)
			colours.Add(BitConverter.ToInt32(rgba, i));
		return colours.Count;
	}

	private static void Keep(GalleryCapture capture, string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		capture.Save(path);
	}

	private static string GoldensDirectory([CallerFilePath] string source = "") =>
		Path.Combine(Path.GetDirectoryName(source)!, "Goldens", "Gallery");
}
