using Shouldly;

using Vysara.Controls;
using Vysara.Extensions;

namespace Vysara.Tests;

public sealed class InputPaneTests
{
	// A 400x1000-pixel surface at scale 2 (200x500 DIPs): an outer border padded 100 DIPs from the top, holding an inner
	// border that fills the rest, from 100 to 500.
	private static (UIRoot Root, Border Inner) Page(bool avoids)
	{
		var inner = new Border();
		InputPane.SetAvoidsKeyboard(inner, avoids);
		var root = new UIRoot(UISurface.Hud)
		{
			LayoutScale = 2f,
			Content = new Border { Padding = new Thickness(0f, 100f, 0f, 0f), Child = inner },
		};
		root.LayoutPass(new Size(400f, 1000f));
		return (root, inner);
	}

	[Test]
	public void An_element_that_avoids_the_keyboard_is_arranged_shorter_by_the_part_under_it()
	{
		var (root, inner) = Page(avoids: true);
		inner.LayoutRect.Height.ShouldBe(400f);

		root.InputPaneHeight = 300f; // pixels: the keyboard's top is at 700 px, 350 DIPs
		root.LayoutPass(new Size(400f, 1000f));

		inner.LayoutRect.Y.ShouldBe(100f); // the border's padding, so the top stays where it was
		inner.LayoutRect.Height.ShouldBe(250f);
	}

	[Test]
	public void The_element_gets_its_height_back_when_the_keyboard_goes()
	{
		var (root, inner) = Page(avoids: true);
		root.InputPaneHeight = 300f;
		root.LayoutPass(new Size(400f, 1000f));

		root.InputPaneHeight = 0f;
		root.LayoutPass(new Size(400f, 1000f));

		inner.LayoutRect.Height.ShouldBe(400f);
	}

	[Test]
	public void An_element_that_does_not_avoid_the_keyboard_stays_under_it()
	{
		var (root, inner) = Page(avoids: false);

		root.InputPaneHeight = 300f;
		root.LayoutPass(new Size(400f, 1000f));

		inner.LayoutRect.Height.ShouldBe(400f);
	}

	[Test]
	public void An_element_above_the_keyboard_is_left_as_it_is()
	{
		var (root, inner) = Page(avoids: true);

		root.InputPaneHeight = 1100f; // the whole surface and more: everything is under it, so the element has no height left
		root.LayoutPass(new Size(400f, 1000f));
		inner.LayoutRect.Height.ShouldBe(0f);

		var above = new Border { Height = 50f, VerticalAlignment = VerticalAlignment.Top };
		InputPane.SetAvoidsKeyboard(above, true);
		var top = new UIRoot(UISurface.Hud) { Content = above, InputPaneHeight = 100f };
		top.LayoutPass(new Size(200f, 500f));
		above.LayoutRect.Height.ShouldBe(50f);
	}

	[Test]
	public void A_bottom_margin_already_above_the_keyboard_is_not_shrunk_again()
	{
		var inner = new Border { Margin = new Thickness(0f, 0f, 0f, 200f) }; // its box ends at 300 DIPs
		InputPane.SetAvoidsKeyboard(inner, true);
		var root = new UIRoot(UISurface.Hud)
		{
			LayoutScale = 2f,
			Content = new Border { Padding = new Thickness(0f, 100f, 0f, 0f), Child = inner },
			InputPaneHeight = 300f, // the keyboard's top at 350 DIPs, below the box
		};
		root.LayoutPass(new Size(400f, 1000f));

		inner.LayoutRect.Height.ShouldBe(200f); // 100 to 300, untouched
	}
}
