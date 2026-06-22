using System.Numerics;
using SUIsei;
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

	void RenderOne(int i) { renderer.Compose(pixelW, pixelH, i / 60.0); renderer.PresentTo(bgra, forceFull: true); }

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

// Self-test: two separate guarantees. (1) The parallel raster must be byte-identical to the serial software-
// rasterizer reference (resolved through the same exact encode) — that's where a tiling bug would show. (2) The
// present path's resolve LUT is a bounded ≤1-LSB approximation of the exact sRGB encode (a uniform LUT cannot be
// bit-identical to MathF.Pow at the 255 rounding boundaries).
//   SUIsei.Gallery --selftest [--size 960x720] [--scale 1]
if (args.Contains("--selftest"))
{
	var (dipW, dipH) = TryGetArg(args, "--size", out var ssize) && TryParseSize(ssize, out var sp) ? sp : (960, 720);
	var scale = TryGetArg(args, "--scale", out var ss) && float.TryParse(ss, out var ssc) && ssc > 0f ? ssc : 1f;
	var pixelW = (int)MathF.Round(dipW * scale);
	var pixelH = (int)MathF.Round(dipH * scale);

	var renderer = new GalleryRenderer(static () => { }) { Scale = scale };
	var serial = renderer.Render(pixelW, pixelH, 0d);              // serial raster + exact resolve (RGBA)
	var parallel = renderer.RenderParallelRgba(pixelW, pixelH, 0d); // parallel raster + exact resolve (RGBA)
	renderer.Compose(pixelW, pixelH, 0d);
	var bgra = new byte[pixelW * pixelH * 4];
	renderer.PresentTo(bgra, forceFull: true);                    // parallel raster + LUT resolve (BGRA)

	int rasterDiff = 0, lutDiff = 0;
	for (long i = 0; i < (long)pixelW * pixelH; i++)
	{
		var o = i * 4;
		for (var ch = 0; ch < 4; ch++)
			rasterDiff = Math.Max(rasterDiff, Math.Abs(serial[o + ch] - parallel[o + ch]));
		lutDiff = Math.Max(lutDiff, Math.Abs(parallel[o + 0] - bgra[o + 2])); // R vs B
		lutDiff = Math.Max(lutDiff, Math.Abs(parallel[o + 1] - bgra[o + 1])); // G
		lutDiff = Math.Max(lutDiff, Math.Abs(parallel[o + 2] - bgra[o + 0])); // B vs R
		lutDiff = Math.Max(lutDiff, Math.Abs(parallel[o + 3] - bgra[o + 3])); // A
	}

	Console.WriteLine($"selftest {pixelW}x{pixelH} @ {scale:0.##}x");
	Console.WriteLine($"  raster (parallel vs serial, exact resolve) : max diff {rasterDiff}  (must be 0)");
	Console.WriteLine($"  resolve LUT (vs exact sRGB encode)         : max diff {lutDiff}  (must be <= 1)");
	var ok = rasterDiff == 0 && lutDiff <= 1;
	Console.WriteLine(ok ? "PASS" : "FAIL");
	Environment.Exit(ok ? 0 : 1);
}

// Damage self-test: prove the dirty-rectangle present is correct. Two renderers are fed an identical input sequence
// (a pointer sweep + a click, advancing time so hover animations run); one repaints only the compositor's damage
// rectangle each frame, the other repaints the whole frame. Their buffers must match every frame — if the damage rect
// ever missed a changed pixel, they would diverge.
//   SUIsei.Gallery --selftest-damage [--size 960x720] [--scale 1] [--frames 48]
if (args.Contains("--selftest-damage"))
{
	var (dipW, dipH) = TryGetArg(args, "--size", out var dsize) && TryParseSize(dsize, out var dp) ? dp : (960, 720);
	var scale = TryGetArg(args, "--scale", out var ds) && float.TryParse(ds, out var dsc) && dsc > 0f ? dsc : 1f;
	var frames = TryGetArg(args, "--frames", out var df) && int.TryParse(df, out var dn) ? Math.Max(2, dn) : 48;
	var pixelW = (int)MathF.Round(dipW * scale);
	var pixelH = (int)MathF.Round(dipH * scale);

	var damaged = new GalleryRenderer(static () => { }) { Scale = scale };  // repaints only the damage rect
	var reference = new GalleryRenderer(static () => { }) { Scale = scale }; // repaints the whole frame each time
	var bgraDamaged = new byte[pixelW * pixelH * 4];
	var bgraReference = new byte[pixelW * pixelH * 4];

	var maxDiff = 0;
	var partialFrames = 0;
	double partialFraction = 0, partialMs = 0;
	for (var i = 0; i < frames; i++)
	{
		// Crawl the pointer down the interactive left column (in DIPs, scaled to physical pixels) so it dwells on the
		// buttons and checkboxes — hover state animates their backgrounds, producing real partial-damage frames — with
		// a press/release midway. Feed the exact same events to both renderers so their content stays in lockstep.
		var pos = new Vector2(80f * scale, i * 9f * scale);
		damaged.QueuePointer(PointerInput.Move(pos)); reference.QueuePointer(PointerInput.Move(pos));
		if (i == frames / 3) { damaged.QueuePointer(PointerInput.Press(pos)); reference.QueuePointer(PointerInput.Press(pos)); }
		if (i == frames / 3 + 2) { damaged.QueuePointer(PointerInput.Release(pos)); reference.QueuePointer(PointerInput.Release(pos)); }

		var t = i / 60.0;
		damaged.Compose(pixelW, pixelH, t);
		var region = damaged.PresentTo(bgraDamaged, forceFull: false);
		reference.Compose(pixelW, pixelH, t);
		reference.PresentTo(bgraReference, forceFull: true);

		if (region.Width < pixelW || region.Height < pixelH)
		{
			partialFrames++;
			partialFraction += region.Width * (double)region.Height / ((double)pixelW * pixelH);
			partialMs += damaged.LastTimings.RasterMs + damaged.LastTimings.ResolveMs;
		}
		for (long p = 0; p < (long)pixelW * pixelH * 4; p++)
			maxDiff = Math.Max(maxDiff, Math.Abs(bgraDamaged[p] - bgraReference[p]));
	}

	Console.WriteLine($"selftest-damage {pixelW}x{pixelH} @ {scale:0.##}x, {frames} frames");
	Console.WriteLine($"  damaged vs full-repaint : max diff {maxDiff}  (must be 0)");
	Console.WriteLine($"  partial-damage frames   : {partialFrames}/{frames}" +
		(partialFrames > 0 ? $" (avg {partialFraction / partialFrames * 100:0.0}% of the frame, {partialMs / partialFrames:0.000} ms raster+resolve)" : ""));
	Console.WriteLine(maxDiff == 0 ? "PASS" : "FAIL - dirty rect missed a changed pixel");
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
