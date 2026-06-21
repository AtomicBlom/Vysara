using SUIsei.Gallery;
using SUIsei.Rasterizer;

// Headless capture: render the gallery offscreen with the same renderer the window uses and write a BMP, then exit.
// Lets the gallery be verified without opening a window (mirrors Loom.Sandbox's --capture convention). --size is the
// DIP size and --scale the DPI scale, so --scale 2 reproduces a 200%-display render (the same LayoutScale path DPI
// awareness drives) at twice the pixels.
//   SUIsei.Gallery --capture out.bmp [--size 960x720] [--scale 1] [--frames 1]
if (TryGetArg(args, "--capture", out var capturePath))
{
	var (dipW, dipH) = TryGetArg(args, "--size", out var size) && TryParseSize(size, out var parsed) ? parsed : (960, 720);
	var scale = TryGetArg(args, "--scale", out var s) && float.TryParse(s, out var sc) && sc > 0f ? sc : 1f;
	var frames = TryGetArg(args, "--frames", out var f) && int.TryParse(f, out var n) ? Math.Max(1, n) : 1;

	var pixelW = (int)MathF.Round(dipW * scale);
	var pixelH = (int)MathF.Round(dipH * scale);
	var renderer = new GalleryRenderer(static () => { }) { Scale = scale };
	byte[] rgba = [];
	for (var i = 0; i < frames; i++)
		rgba = renderer.Render(pixelW, pixelH, i / 60.0); // step time so any animation advances frame to frame

	BmpWriter.Write(capturePath!, pixelW, pixelH, rgba);
	Console.WriteLine($"captured {pixelW}x{pixelH} @ {scale:0.##}x ({frames} frame(s)) -> {capturePath}");
	return;
}

// Headless benchmark: time the steady-state present path (the same parallel raster + resolve the window uses) per
// phase, no window. Mirrors --capture's size/scale knobs. Run this in Release — Debug is many times slower.
//   SUIsei.Gallery --bench [--size 960x720] [--scale 1] [--frames 30]
if (args.Contains("--bench"))
{
	var (dipW, dipH) = TryGetArg(args, "--size", out var bsize) && TryParseSize(bsize, out var bp) ? bp : (960, 720);
	var scale = TryGetArg(args, "--scale", out var bs) && float.TryParse(bs, out var bsc) && bsc > 0f ? bsc : 1f;
	var frames = TryGetArg(args, "--frames", out var bf) && int.TryParse(bf, out var bn) ? Math.Max(1, bn) : 30;

	var pixelW = (int)MathF.Round(dipW * scale);
	var pixelH = (int)MathF.Round(dipH * scale);
	var renderer = new GalleryRenderer(static () => { }) { Scale = scale };
	var bgra = new byte[pixelW * pixelH * 4];

	void RenderOne(int i) { renderer.Compose(pixelW, pixelH, i / 60.0); renderer.PresentTo(bgra); }

	for (var i = 0; i < 10; i++) // warm up (JIT + thread-pool spin-up + first-frame allocations)
		RenderOne(i);

	double tick = 0, compose = 0, raster = 0, resolve = 0;
	var sw = System.Diagnostics.Stopwatch.StartNew();
	for (var i = 0; i < frames; i++)
	{
		RenderOne(i);
		var t = renderer.LastTimings;
		tick += t.TickMs; compose += t.ComposeMs; raster += t.RasterMs; resolve += t.ResolveMs;
	}
	sw.Stop();

	var perFrame = sw.Elapsed.TotalMilliseconds / frames;
	Console.WriteLine($"bench {pixelW}x{pixelH} @ {scale:0.##}x, {frames} frames ({Environment.ProcessorCount} cores)");
	Console.WriteLine($"  total : {perFrame,7:0.00} ms/frame  ({1000.0 / perFrame:0.0} fps, {pixelW * (long)pixelH / perFrame / 1000:0.0} Mpix/s)");
	Console.WriteLine($"  tick  : {tick / frames,7:0.00} ms");
	Console.WriteLine($"  compose:{compose / frames,7:0.00} ms");
	Console.WriteLine($"  raster: {raster / frames,7:0.00} ms");
	Console.WriteLine($"  resolve:{resolve / frames,7:0.00} ms");
	return;
}

// Self-test: prove the parallel present path is byte-identical to the serial software-rasterizer reference.
//   SUIsei.Gallery --selftest [--size 960x720] [--scale 1]
if (args.Contains("--selftest"))
{
	var (dipW, dipH) = TryGetArg(args, "--size", out var ssize) && TryParseSize(ssize, out var sp) ? sp : (960, 720);
	var scale = TryGetArg(args, "--scale", out var ss) && float.TryParse(ss, out var ssc) && ssc > 0f ? ssc : 1f;
	var pixelW = (int)MathF.Round(dipW * scale);
	var pixelH = (int)MathF.Round(dipH * scale);

	var renderer = new GalleryRenderer(static () => { }) { Scale = scale };
	var rgba = renderer.Render(pixelW, pixelH, 0d);          // serial reference (RGBA)
	renderer.Compose(pixelW, pixelH, 0d);
	var bgra = new byte[pixelW * pixelH * 4];
	renderer.PresentTo(bgra);                                // parallel present (BGRA)

	var maxDiff = 0;
	for (long i = 0; i < (long)pixelW * pixelH; i++)
	{
		var o = i * 4;
		maxDiff = Math.Max(maxDiff, Math.Abs(rgba[o + 0] - bgra[o + 2])); // R vs B
		maxDiff = Math.Max(maxDiff, Math.Abs(rgba[o + 1] - bgra[o + 1])); // G
		maxDiff = Math.Max(maxDiff, Math.Abs(rgba[o + 2] - bgra[o + 0])); // B vs R
		maxDiff = Math.Max(maxDiff, Math.Abs(rgba[o + 3] - bgra[o + 3])); // A
	}
	Console.WriteLine($"selftest {pixelW}x{pixelH} @ {scale:0.##}x: max channel diff (parallel vs serial) = {maxDiff}");
	Console.WriteLine(maxDiff == 0 ? "PASS - byte-identical" : "FAIL - parallel path diverges from the serial oracle");
	Environment.Exit(maxDiff == 0 ? 0 : 1);
}

var window = new Win32Window();
window.Create(960, 720, "SUIsei — Control Gallery (software-rendered)");
window.RunLoop();

static bool TryGetArg(string[] args, string name, out string? value)
{
	for (var i = 0; i < args.Length - 1; i++)
	{
		if (args[i] == name)
		{
			value = args[i + 1];
			return true;
		}
	}
	value = null;
	return false;
}

static bool TryParseSize(string? s, out (int Width, int Height) size)
{
	size = default;
	var parts = s?.Split('x', 'X');
	if (parts is { Length: 2 } && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 && h > 0)
	{
		size = (w, h);
		return true;
	}
	return false;
}
