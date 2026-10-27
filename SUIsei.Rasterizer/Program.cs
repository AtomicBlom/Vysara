using System.Diagnostics;
using SUIsei;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Rasterizer;

const int W = 480;
const int H = 140;

// A REAL compositor scene — no hand-authored geometry. A rounded panel (RoundedRect fill + rounded Border stroke)
// wrapping a TextBlock (MTSDF Glyph run). Exercises the analytic kinds AND the atlas-sampled text path.
var label = new TextBlock
{
	Text = "Hello, SUIsei",
	FontSize = 44f,
	Foreground = new SolidColorBrush(HdrColor.FromRgb(235, 238, 245)),
};
var panel = new Border
{
	CornerRadius = new CornerRadius(16f),
	Background = new SolidColorBrush(HdrColor.FromRgb(30, 33, 40)),
	BorderBrush = new SolidColorBrush(HdrColor.FromRgb(90, 120, 200)),
	BorderThickness = new Thickness(3f),
	Padding = new Thickness(20f),
	Child = label,
};

var root = new UIRoot(new DrawListId(0)) { Content = panel };
root.LayoutPass(new Size(W, H));
var draw = root.Render();

Console.WriteLine("── Draw list (the neutral contract the rasterizer consumes) ─────────────");
Console.WriteLine(draw.Dump());

var clear = HdrColor.FromRgb(245, 245, 247).ToPremultiplied().ToVector4(); // linear, premultiplied
var rast = new SoftwareRasterizer(W, H);

// Warm once, then time a steady-state repaint loop to show the perf shape.
rast.Clear(clear);
rast.Render(draw);

const int iters = 300;
var sw = Stopwatch.StartNew();
for (var n = 0; n < iters; n++)
{
	rast.Clear(clear);
	rast.Render(draw);
}
sw.Stop();

var px = rast.ResolveToRgba8();

var outDir = Environment.GetEnvironmentVariable("SPIKE_OUT") ?? Path.GetTempPath();
var outPath = Path.Combine(outDir, "suisei-raster-spike.bmp");
BmpWriter.Write(outPath, W, H, px);

Console.WriteLine("── ASCII preview (luminance) ────────────────────────────────────────────");
AsciiPreview(px, W, H, 96);

Console.WriteLine("── Stats ────────────────────────────────────────────────────────────────");
var perFrameUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iters;
Console.WriteLine($"triangles/frame : {rast.TrianglesDrawn}");
Console.WriteLine($"skipped (unbound tex) : {rast.SkippedCommands}");
Console.WriteLine($"resolution      : {W}x{H} ({W * H} px)");
Console.WriteLine($"time/frame      : {perFrameUs:0.0} µs  ({W * H / perFrameUs:0.0} Mpix/s, full repaint)");
Console.WriteLine($"bmp written     : {outPath}");

Console.WriteLine("── Geometry fidelity (sRGB round-trip vs authored colours) ──────────────");
var ok = true;
ok &= Check("rounded corner is clear bg", px, W, 3, 3, 245, 245, 247, tol: 6);
ok &= Check("top-mid is panel border", px, W, 240, 1, 90, 120, 200, tol: 8);
ok &= Check("below text is panel fill", px, W, 240, 118, 30, 33, 40, tol: 4);

Console.WriteLine("── Glyph fidelity (text region 24..456 × 24..116 over the dark panel) ───");
var (ink, aa, fgHit) = AnalyseText(px, W, 24, 24, 456, 116);
Console.WriteLine($"  atlas bound (0 skipped) : {rast.SkippedCommands == 0}");
Console.WriteLine($"  ink pixels (lum>0.30)   : {ink}");
Console.WriteLine($"  anti-aliased edge px    : {aa}  (intermediate coverage → MTSDF AA working)");
Console.WriteLine($"  near-foreground px      : {fgHit}  (fully-covered glyph interiors)");
ok &= Report("atlas bound", rast.SkippedCommands == 0);
ok &= Report("text drew ink (>400 px)", ink > 400);
ok &= Report("glyph edges anti-aliased (>40 px)", aa > 40);
ok &= Report("solid glyph interiors present (>40 px)", fgHit > 40);

Console.WriteLine(ok ? "\nALL CHECKS PASSED" : "\nSOME CHECKS FAILED");
return ok ? 0 : 1;

static bool Check(string label, byte[] px, int width, int x, int y, int er, int eg, int eb, int tol)
{
	var o = (y * width + x) * 4;
	int r = px[o], g = px[o + 1], b = px[o + 2];
	var pass = Math.Abs(r - er) <= tol && Math.Abs(g - eg) <= tol && Math.Abs(b - eb) <= tol;
	Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {label,-26} @({x},{y})  got ({r},{g},{b})  want ({er},{eg},{eb}) ±{tol}");
	return pass;
}

static bool Report(string label, bool pass)
{
	Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {label}");
	return pass;
}

// Count text "ink" over the dark panel: total lit pixels, anti-aliased (intermediate) pixels, and near-foreground
// (fully covered) pixels — proves both that glyphs drew and that the MTSDF coverage ramp is doing real AA.
static (int ink, int aa, int fgHit) AnalyseText(byte[] px, int width, int x0, int y0, int x1, int y1)
{
	int ink = 0, aa = 0, fgHit = 0;
	for (var y = y0; y <= y1; y++)
	{
		for (var x = x0; x <= x1; x++)
		{
			var lum = Lum(px, width, x, y);
			if (lum > 0.30f) ink++;
			if (lum is > 0.15f and < 0.70f) aa++;
			if (lum > 0.80f) fgHit++;
		}
	}
	return (ink, aa, fgHit);
}

static void AsciiPreview(byte[] px, int width, int height, int cols)
{
	const string ramp = " .:-=+*#%@";
	var rows = Math.Max(1, (int)(cols * (height / (float)width) * 0.5f)); // 0.5 ≈ char aspect
	for (var ry = 0; ry < rows; ry++)
	{
		var line = new char[cols];
		for (var rx = 0; rx < cols; rx++)
		{
			var sx = Math.Min(width - 1, rx * width / cols);
			var sy = Math.Min(height - 1, ry * height / rows);
			var lum = Lum(px, width, sx, sy);
			line[rx] = ramp[Math.Clamp((int)(lum * (ramp.Length - 1) + 0.5f), 0, ramp.Length - 1)];
		}
		Console.WriteLine(new string(line));
	}
}

static float Lum(byte[] px, int width, int x, int y)
{
	var o = (y * width + x) * 4;
	return (0.2126f * px[o] + 0.7152f * px[o + 1] + 0.0722f * px[o + 2]) / 255f;
}
