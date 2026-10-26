using System.Numerics;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// WinUI path markup through <see cref="Geometry.Parse"/>: each command draws what it describes (relative forms, repeats,
/// smooth curves, arcs, closes, the fill rule), markup round-trips through the geometry it builds, and every mistake is
/// reported at its character.
/// </summary>
public sealed class PathMarkupTests
{
	[Test]
	[Arguments("M 0,0 L 10,0", "M 0,0 L 10,0")]
	[Arguments("m 1,2 l 3,4 5,6", "M 1,2 L 4,6 L 9,12")]
	[Arguments("M0 0 10 0 10 10", "M 0,0 L 10,0 L 10,10")]
	[Arguments("m 1,1 2,2", "M 1,1 L 3,3")]
	[Arguments("M 0,0 H 5 V 6 h 1 v -1", "M 0,0 L 5,0 L 5,6 L 6,6 L 6,5")]
	[Arguments("M 0,0 C 1,1 2,2 3,3 S 5,5 6,6", "M 0,0 C 1,1 2,2 3,3 C 4,4 5,5 6,6")]
	[Arguments("M 0,0 L 1,0 S 5,5 6,6", "M 0,0 L 1,0 C 1,0 5,5 6,6")]
	[Arguments("M 0,0 c 1,1 2,2 3,3 s 2,2 3,3", "M 0,0 C 1,1 2,2 3,3 C 4,4 5,5 6,6")]
	[Arguments("M 0,0 Q 1,2 3,0 T 6,0", "M 0,0 Q 1,2 3,0 Q 5,-2 6,0")]
	[Arguments("M 0,0 T 6,0", "M 0,0 Q 0,0 6,0")]
	[Arguments("M 0,0 L 1,0 Z M 5,5 L 6,5 z", "M 0,0 L 1,0 Z M 5,5 L 6,5 Z")]
	[Arguments("M 2,2 L 3,2 Z m 1,1 l 1,0", "M 2,2 L 3,2 Z M 3,3 L 4,3")]
	[Arguments("F1 M 0,0 L 1,1", "F1 M 0,0 L 1,1")]
	[Arguments("F0 M 0,0 L 1,1", "M 0,0 L 1,1")]
	[Arguments("M10-5L.5.5", "M 10,-5 L 0.5,0.5")]
	[Arguments("M 1e1,2E-1 L 0,0", "M 10,0.2 L 0,0")]
	[Arguments("  M\t0 , 0\r\nL 1 1  ", "M 0,0 L 1,1")]
	public void Markup_draws_the_path_it_describes(string markup, string expected) =>
		Geometry.Parse(markup).CompositionPath.ToString().ShouldBe(expected);

	[Test]
	public void Runs_of_one_command_become_poly_segments_and_single_commands_plain_ones()
	{
		var geometry = Geometry.Parse("F1 M 0,0 L 1,0 L 2,0 3,0 C 1,1 2,2 3,3 Q 4,4 5,5 6,6 7,7 A 2,3 15 1 1 9,9 L 10,10 Z M 20,20 Q 1,1 2,2");

		geometry.FillRule.ShouldBe(FillRule.Nonzero);
		geometry.Figures.Count.ShouldBe(2);
		var first = geometry.Figures[0];
		first.StartPoint.ShouldBe(new Point(0, 0));
		first.IsClosed.ShouldBeTrue();
		first.Segments.Select(s => s.GetType().Name).ShouldBe(
			["PolyLineSegment", "BezierSegment", "PolyQuadraticBezierSegment", "ArcSegment", "LineSegment"]);
		((PolyLineSegment)first.Segments[0]).Points.ShouldBe([new Point(1, 0), new Point(2, 0), new Point(3, 0)]);
		var arc = (ArcSegment)first.Segments[3];
		(arc.Size, arc.RotationAngle, arc.IsLargeArc, arc.SweepDirection, arc.Point)
			.ShouldBe((new Size(2, 3), 15f, true, SweepDirection.Clockwise, new Point(9, 9)));
		geometry.Figures[1].IsClosed.ShouldBeFalse();
		geometry.Figures[1].Segments.ShouldHaveSingleItem().ShouldBeOfType<QuadraticBezierSegment>();
	}

	[Test]
	[Arguments("F1 M 0,0 L 10,0 10,10 C 1.5,2.25 3,4 5,6 Q 7,8 9,10 A 5,7 30 1 0 20,20 Z M -1,-2 L 3.14159,2.71828")]
	[Arguments("m 1 1 h 10 v 10 h -10 z m 2 2 l 1 1 s 1 2 3 4 t 5 6 a 1 1 0 0 1 2 0")]
	[Arguments("M 0.1,0.2 L 0.30000001,1e-5")]
	public void Markup_round_trips_through_the_geometry(string markup)
	{
		var geometry = Geometry.Parse(markup);
		var written = geometry.ToMarkup();
		var reparsed = Geometry.Parse(written);

		reparsed.ToMarkup().ShouldBe(written);
		reparsed.CompositionPath.ToString().ShouldBe(geometry.CompositionPath.ToString());
		reparsed.FillRule.ShouldBe(geometry.FillRule);
	}

	[Test]
	public void An_arc_runs_the_way_its_sweep_says_and_lies_on_its_circle()
	{
		var clockwise = Geometry.Parse("M 0,0 A 5,5 0 0 1 10,0");
		var counter = Geometry.Parse("M 0,0 A 5,5 0 0 0 10,0");

		// With y down, clockwise from the left end passes over the top.
		clockwise.Bounds.ShouldBeNear(new Rect(0, -5, 10, 5), 1e-4f);
		counter.Bounds.ShouldBeNear(new Rect(0, 0, 10, 5), 1e-4f);
		foreach (var p in Samples(clockwise.CompositionPath))
			Vector2.Distance(p, new Vector2(5, 0)).ShouldBe(5f, 1e-4f);
	}

	[Test]
	[Arguments("", 1, "the path has no figures")]
	[Arguments("   ", 4, "the path has no figures")]
	[Arguments("L 1,1", 1, "'L' comes before any move")]
	[Arguments("M 0,0 L 1", 7, "'L' needs 2 numbers, x and y")]
	[Arguments("M 0,0 L 1,1 2", 7, "for each repeat")]
	[Arguments("M 0,0 C 1,1 2,2", 7, "'C' needs 6 numbers")]
	[Arguments("M 0,0 X 1", 7, "'X' is not a path command")]
	[Arguments("M 0,0 Z L 1,1", 9, "'L' follows a close")]
	[Arguments("F2 M 0,0", 1, "a fill rule is F0")]
	[Arguments("F1M 0,0 F1", 9, "'F' is not a path command")]
	[Arguments("M 0,0 A 5,5 0 2 0 10,0", 15, "large-arc flag is 0 or 1")]
	[Arguments("M 0,0 A 5,5 0 1 0.5 10,0", 17, "sweep-direction flag is 0 or 1")]
	[Arguments("M 0,0 A -5,5 0 0 0 10,0", 7, "radii of 0 or more")]
	[Arguments("M 0,0 L NaN,1", 7, "'L' needs 2 numbers")]
	[Arguments("M 0,0 L 1e39,1", 7, "'L' needs 2 numbers")]
	[Arguments("5 M 0,0", 1, "a number with no command before it")]
	[Arguments("M", 1, "'M' needs 2 numbers")]
	public void A_mistake_is_reported_at_its_character(string markup, int character, string message)
	{
		var ex = Should.Throw<FormatException>(() => Geometry.Parse(markup));
		ex.Message.ShouldContain($"at character {character}: ");
		ex.Message.ShouldContain(message);
	}

	[Test]
	[Arguments("F1 M 0,0 L 1,1")]
	[Arguments("f0M0,0z")]
	[Arguments("M 0,0 C 1,1 2,2 3,3 S 4,4 5,5 Q 1,1 2,2 T 3,3 A 1,1 0 0 1 4,4 H 1 V 2 Z")]
	[Arguments("M 0,0 L 1")]
	[Arguments("F2 M 0,0")]
	[Arguments("M 0,0 A 5,5 0 2 0 10,0")]
	[Arguments("")]
	public void Validating_without_a_sink_agrees_with_parsing(string markup)
	{
		// The generator validates with no sink; it must reach the verdict and message the runtime's parse does.
		var valid = SUIsei.Markup.PathMarkup.TryParse(markup, null, out var error);
		var parseError = Throws(() => Geometry.Parse(markup))?.Message;
		valid.ShouldBe(parseError is null);
		if (!valid)
			parseError.ShouldNotBeNull().ShouldContain(error);

		static FormatException? Throws(Action action)
		{
			try
			{
				action();
				return null;
			}
			catch (FormatException ex)
			{
				return ex;
			}
		}
	}

	// Points along every curve of a path, at its ends and between.
	private static IEnumerable<Vector2> Samples(CompositionPath path)
	{
		var verbs = path.Verbs.ToArray();
		var points = path.Points.ToArray();
		var p = 0;
		var last = Vector2.Zero;
		foreach (var verb in verbs)
		{
			switch (verb)
			{
				case PathVerb.Move or PathVerb.Line:
					last = points[p++];
					yield return last;
					break;
				case PathVerb.Cubic:
					{
						var (c1, c2, e) = (points[p], points[p + 1], points[p + 2]);
						p += 3;
						for (var t = 0.05f; t <= 1f; t += 0.05f)
						{
							var u = 1 - t;
							yield return u * u * u * last + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * e;
						}
						last = e;
						break;
					}
			}
		}
	}
}
