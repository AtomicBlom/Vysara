using Shouldly;

using SUIsei.Controls;
using SUIsei.Extensions;

namespace SUIsei.Tests;

public sealed class SafeAreaTests
{
	// A 400x1000-pixel surface at scale 2 (200x500 DIPs) whose top 80 px and bottom 40 px are hidden, as a phone's
	// cutout and gesture bar hide them: the safe part is 40 to 480 DIPs down.
	private static readonly Size Surface = new(400f, 1000f);
	private static readonly Thickness Hidden = new(0f, 80f, 0f, 40f);

	private static Border LaidOut(SafeAreaEdges edges, Thickness insets, Thickness margin = default)
	{
		var page = new Border { Margin = margin };
		SafeArea.SetEdges(page, edges);
		var root = new UIRoot(UISurface.Hud) { LayoutScale = 2f, Content = page, SafeAreaInsets = insets };
		root.LayoutPass(Surface);
		return page;
	}

	[Test]
	public void An_element_on_every_edge_is_arranged_inside_the_safe_part()
	{
		var page = LaidOut(SafeAreaEdges.All, Hidden);

		page.LayoutRect.ShouldBe(new Rect(0f, 40f, 200f, 440f));
	}

	[Test]
	public void Only_the_named_edges_are_kept_clear()
	{
		var page = LaidOut(SafeAreaEdges.Top, Hidden);

		page.LayoutRect.ShouldBe(new Rect(0f, 40f, 200f, 460f)); // the bottom still runs under the hidden band
	}

	[Test]
	public void An_element_that_names_no_edge_runs_to_the_edges()
	{
		var page = LaidOut(SafeAreaEdges.None, Hidden);

		page.LayoutRect.ShouldBe(new Rect(0f, 0f, 200f, 500f));
	}

	[Test]
	public void An_element_already_clear_of_the_bands_is_left_as_it_is()
	{
		var page = LaidOut(SafeAreaEdges.All, Hidden, margin: new Thickness(0f, 60f, 0f, 30f));

		page.LayoutRect.ShouldBe(new Rect(0f, 60f, 200f, 410f));
	}

	[Test]
	public void The_insets_moving_arranges_the_element_again()
	{
		var page = new Border();
		SafeArea.SetEdges(page, SafeAreaEdges.All);
		var root = new UIRoot(UISurface.Hud) { LayoutScale = 2f, Content = page, SafeAreaInsets = Hidden };
		root.LayoutPass(Surface);

		root.SafeAreaInsets = new Thickness(60f, 0f, 20f, 0f); // rotated: the cutout is on the left now
		root.LayoutPass(Surface);

		page.LayoutRect.ShouldBe(new Rect(30f, 0f, 160f, 500f));
	}
}
