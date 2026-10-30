using System.Numerics;
using SUIsei.Composition;
using SUIsei.Drawing;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// Trimming at emit: <see cref="PathTrim"/>'s WinUI semantics, a trimmed stroke drawing exactly what stroking the
/// trimmed part of the path would draw, wrapping round a closed figure through its closing join, all from one
/// tessellation and with no allocation.
/// </summary>
public sealed class TrimTests
{
	private struct Collect() : IVectorSink
	{
		public List<VectorVertex> Vertices { get; } = [];

		public void AddTriangles(ReadOnlySpan<VectorVertex> vertices) => Vertices.AddRange(vertices);
	}

	private struct Count : IVectorSink
	{
		public int Vertices;

		public void AddTriangles(ReadOnlySpan<VectorVertex> vertices) => Vertices += vertices.Length;
	}

	private static CompositionStrokeStyle Style(PenLineCap start, PenLineCap end, PenLineJoin join = PenLineJoin.Miter) =>
		new() { Thickness = 4, StartCap = start, EndCap = end, LineJoin = join };

	private static TessellatedPath Stroke(string markup, CompositionStrokeStyle style)
	{
		var output = new TessellatedPath();
		new Tessellator().Stroke(Geometry.Parse(markup).CompositionPath, style, TessellationScale.Identity, output);
		return output;
	}

	private static List<VectorVertex> Emit(TessellatedPath path, PathTrim trim)
	{
		var sink = new Collect();
		path.Emit(ref sink, trim);
		return sink.Vertices;
	}

	// The triangles as a sorted, comparable list: positions and distances, rounded, arc length left out (it is measured
	// from a different start).
	private static string[] Canonical(IReadOnlyList<VectorVertex> vertices)
	{
		var triangles = new List<string>();
		for (var i = 0; i < vertices.Count; i += 3)
		{
			var corners = Enumerable.Range(i, 3).Select(k => Format(vertices[k])).Order(StringComparer.Ordinal);
			triangles.Add(string.Join(" | ", corners));
		}
		triangles.Sort(StringComparer.Ordinal);
		return [.. triangles];

		static string Format(VectorVertex v)
		{
			static string R(float f) => f >= VectorVertex.Unused ? "-" : MathF.Round(f, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
			return $"({R(v.Position.X)},{R(v.Position.Y)}) [{R(v.Distances.X)},{R(v.Distances.Y)},{R(v.Distances.Z)},{R(v.Distances.W)}]";
		}
	}

	[Test]
	[Arguments(0f, 1f, 0f, 1, 0f, 1f)]
	[Arguments(0.25f, 0.75f, 0f, 1, 0.25f, 0.75f)]
	[Arguments(0.75f, 0.25f, 0f, 1, 0.25f, 0.75f)]  // swapped
	[Arguments(-1f, 2f, 0f, 1, 0f, 1f)]             // clamped
	[Arguments(0f, 0.5f, 0.75f, 2, 0f, 0.25f)]      // wraps: [0.75, 1] and [0, 0.25]
	[Arguments(0f, 0.5f, 1.25f, 1, 0.25f, 0.75f)]   // offsets wrap whole turns
	[Arguments(0f, 0.5f, -0.25f, 2, 0f, 0.25f)]     // a negative offset wraps the other way
	[Arguments(0.3f, 0.3f, 0f, 0, 0f, 0f)]          // nothing
	public void Trim_follows_WinUIs_start_end_and_offset(float start, float end, float offset, int count, float from, float to)
	{
		var trim = new PathTrim(start, end, offset);
		trim.Visible(out var first, out var second).ShouldBe(count);
		if (count == 0)
			return;
		first.From.ShouldBe(from, 1e-6f);
		first.To.ShouldBe(to, 1e-6f);
		if (count == 2)
			second.ShouldBe((0.75f, 1f));
	}

	[Test]
	public void None_draws_the_whole_stroke_and_IsNone_ignores_the_offset()
	{
		PathTrim.None.IsNone.ShouldBeTrue();
		new PathTrim(0, 1, 0.4f).IsNone.ShouldBeTrue();
		new PathTrim(0, 0.99f, 0).IsNone.ShouldBeFalse();
		var path = Stroke("M 0,0 L 10,0 10,10", Style(PenLineCap.Round, PenLineCap.Round));
		Emit(path, PathTrim.None).Count.ShouldBe(path.Vertices.Length);
	}

	[Test]
	[Arguments(PenLineCap.Flat, PenLineCap.Flat, PenLineJoin.Miter)]
	[Arguments(PenLineCap.Square, PenLineCap.Round, PenLineJoin.Round)]
	[Arguments(PenLineCap.Triangle, PenLineCap.Square, PenLineJoin.Bevel)]
	public void A_trimmed_stroke_draws_what_stroking_the_trimmed_path_draws(PenLineCap start, PenLineCap end, PenLineJoin join)
	{
		// 80 long; trimming to 0.25..0.75 leaves 20..60: from (20,0) round the corner to (40,20).
		var whole = Stroke("M 0,0 L 40,0 L 40,40", Style(start, end, join));
		var part = Stroke("M 20,0 L 40,0 L 40,20", Style(start, end, join));
		var version = whole.Version;

		var trimmed = Emit(whole, new PathTrim(0.25f, 0.75f, 0f));

		Canonical(trimmed).ShouldBe(Canonical(part.Vertices.ToArray()));
		whole.Version.ShouldBe(version); // drawn from the same tessellation
	}

	[Test]
	public void Trimming_at_a_vertex_starts_a_cap_there_and_leaves_out_its_join()
	{
		var whole = Stroke("M 0,0 L 40,0 L 40,40", Style(PenLineCap.Round, PenLineCap.Round));
		var part = Stroke("M 40,0 L 40,40", Style(PenLineCap.Round, PenLineCap.Round));

		Canonical(Emit(whole, new PathTrim(0.5f, 1f, 0f))).ShouldBe(Canonical(part.Vertices.ToArray()));
	}

	[Test]
	public void Trimming_a_closed_figure_across_its_start_runs_through_the_closing_join()
	{
		// A 40-long square: start 0, end 0.5, offset 0.875 shows 35..40 and 0..15, joined at the start corner.
		var style = Style(PenLineCap.Round, PenLineCap.Round);
		var whole = Stroke("M 0,0 L 10,0 10,10 0,10 Z", style);
		var part = Stroke("M 0,5 L 0,0 L 10,0 L 10,5", style);

		Canonical(Emit(whole, new PathTrim(0f, 0.5f, 0.875f))).ShouldBe(Canonical(part.Vertices.ToArray()));
	}

	[Test]
	public void Trimming_a_closed_figure_inside_it_caps_both_ends()
	{
		var style = Style(PenLineCap.Square, PenLineCap.Triangle);
		var whole = Stroke("M 0,0 L 10,0 10,10 0,10 Z", style);
		var part = Stroke("M 5,0 L 10,0 L 10,10 L 5,10", style);

		Canonical(Emit(whole, new PathTrim(0.125f, 0.625f, 0f))).ShouldBe(Canonical(part.Vertices.ToArray()));
	}

	[Test]
	public void Trim_spans_figures_by_the_whole_geometrys_length()
	{
		// Two 20-long lines: 0.25..0.75 is the second half of the first and the first half of the second.
		var style = Style(PenLineCap.Flat, PenLineCap.Flat);
		var whole = Stroke("M 0,0 L 20,0 M 0,10 L 20,10", style);
		var part = Stroke("M 10,0 L 20,0 M 0,10 L 10,10", style);

		Canonical(Emit(whole, new PathTrim(0.25f, 0.75f, 0f))).ShouldBe(Canonical(part.Vertices.ToArray()));
	}

	[Test]
	public void Trimming_a_dashed_stroke_cuts_its_dashes_with_the_line_caps()
	{
		var style = Style(PenLineCap.Round, PenLineCap.Round);
		style.DashCap = PenLineCap.Square;
		style.SetDashArray([2, 1]); // dashes of 8, gaps of 4
		var whole = Stroke("M 0,0 L 48,0", style);
		var vertices = Emit(whole, new PathTrim(4f / 48f, 30f / 48f, 0f));

		// Visible: 4..8 (cut at 4: round), 12..20, 24..30 (cut at 30: round); each whole dash end is square.
		vertices.Min(v => v.ArcLength).ShouldBe(4f, 1e-4f);
		vertices.Max(v => v.ArcLength).ShouldBe(30f, 1e-4f);
		vertices.ShouldAllBe(v => v.ArcLength <= 8f || (v.ArcLength >= 12f && v.ArcLength <= 20f) || v.ArcLength >= 24f);
	}

	[Test]
	public void A_fill_ignores_the_trim_and_an_empty_trim_draws_nothing()
	{
		var fill = new TessellatedPath();
		new Tessellator().Fill(Geometry.Parse("M 0,0 L 10,0 10,10 Z").CompositionPath, TessellationScale.Identity, fill);
		Emit(fill, new PathTrim(0.2f, 0.4f, 0f)).Count.ShouldBe(fill.Vertices.Length);

		var stroke = Stroke("M 0,0 L 10,0", Style(PenLineCap.Round, PenLineCap.Round));
		Emit(stroke, new PathTrim(0.5f, 0.5f, 0f)).ShouldBeEmpty();
	}

	[Test]
	public void Animating_a_trim_allocates_nothing_and_never_re_tessellates()
	{
		var style = Style(PenLineCap.Round, PenLineCap.Triangle, PenLineJoin.Round);
		style.SetDashArray([3, 1]);
		var path = Stroke("M 0,0 C 0,100 100,100 100,0 L 150,50 Z M 200,0 L 300,0", style);
		var version = path.Version;
		var sink = new Count();
		for (var i = 0; i < 50; i++)
			path.Emit(ref sink, new PathTrim(0, i / 50f, i / 25f));

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 500; i++)
			path.Emit(ref sink, new PathTrim(0.1f, 0.1f + i / 600f, i / 300f));
		(GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
		path.Version.ShouldBe(version);
		sink.Vertices.ShouldBeGreaterThan(0);
	}
}
