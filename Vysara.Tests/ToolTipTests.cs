using System.Numerics;

using Vysara.Controls;
using Vysara.Controls.Primitives;
using Vysara.Input;

namespace Vysara.Tests;

/// <summary>
/// <see cref="ToolTipService"/>: a tooltip shows once the pointer has rested on its element, below it (or above where
/// there's no room), goes as the pointer leaves or presses or a key is pressed, and an element inside one without its own
/// shows the one above. <see cref="AnchoredPlacement"/> keeps what it places inside the surface.
/// </summary>
public sealed class ToolTipTests
{
	private static readonly Vector2 Surface = new(400f, 300f);

	private static (UIHost Host, Button Button, UIRoot Root) Page(float top = 20f)
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var button = new Button
		{
			Width = 100f,
			Height = 30f,
			Margin = new Thickness(50f, top, 0f, 0f),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		ToolTipService.SetToolTip(button, "Undo (Ctrl Z)");
		view.Root.Content = button;
		host.Tick(0.016, 0f, Surface);
		return (host, button, view.Root);
	}

	private static void Rest(UIHost host, double seconds)
	{
		for (var t = 0.0; t < seconds; t += 0.1)
			host.Tick(0.1, 0f, Surface);
	}

	[Test]
	public void It_shows_once_the_pointer_has_rested()
	{
		var (host, _, _) = Page();
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		host.Tick(0.016, 0f, Surface);

		Rest(host, ToolTipService.InitialShowDelay / 2);
		host.ToolTips.Showing.ShouldBeNull();

		Rest(host, ToolTipService.InitialShowDelay);
		host.ToolTips.Showing.ShouldNotBeNull();
		host.ToolTips.Showing!.Content.ShouldBe("Undo (Ctrl Z)");
	}

	[Test]
	public void Long_text_wraps_inside_it()
	{
		var (host, button, _) = Page();
		ToolTipService.SetToolTip(button, string.Join(' ', Enumerable.Repeat("A bar to each line, one chord or two sharing it.", 6)));
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		Rest(host, 1.0);

		var tip = host.ToolTips.Showing!;
		tip.LayoutRect.Width.ShouldBeLessThanOrEqualTo(ToolTip.MaxWidthDefault);
		var text = Text(tip);
		text.TextWrapping.ShouldBe(TextWrapping.Wrap);
		text.LayoutRect.Width.ShouldBeLessThanOrEqualTo(tip.LayoutRect.Width);
		text.LayoutRect.Height.ShouldBeGreaterThan(text.FontSize * 2f);
	}

	private static TextBlock Text(UIElement element)
	{
		if (element is TextBlock text)
			return text;
		foreach (var child in element.VisualChildren)
			if (Find(child) is { } found)
				return found;
		throw new InvalidOperationException("No text in the tooltip.");

		static TextBlock? Find(UIElement e)
		{
			if (e is TextBlock text)
				return text;
			foreach (var child in e.VisualChildren)
				if (Find(child) is { } found)
					return found;
			return null;
		}
	}

	[Test]
	public void It_opens_below_its_element_and_above_where_there_is_no_room()
	{
		var (host, button, _) = Page();
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		Rest(host, 1.0);
		host.ToolTips.Showing!.LayoutRect.Y.ShouldBeGreaterThan(button.LayoutRect.Bottom);

		var (low, lowButton, _) = Page(top: 275f);
		low.QueuePointer(PointerInput.Move(new Vector2(80f, 285f)));
		Rest(low, 1.0);
		low.ToolTips.Showing!.LayoutRect.Bottom.ShouldBeLessThan(lowButton.LayoutRect.Y);
	}

	[Test]
	public void A_press_hides_it_until_the_pointer_comes_back()
	{
		var (host, _, _) = Page();
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		Rest(host, 1.0);

		host.QueuePointer(PointerInput.Press(new Vector2(80f, 30f)));
		host.QueuePointer(PointerInput.Release(new Vector2(80f, 30f)));
		Rest(host, 1.0);
		host.ToolTips.Showing.ShouldBeNull();

		host.QueuePointer(PointerInput.Move(new Vector2(300f, 200f)));
		host.Tick(0.016, 0f, Surface);
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		Rest(host, 1.0);
		host.ToolTips.Showing.ShouldNotBeNull();
	}

	[Test]
	public void A_press_held_and_let_go_on_the_same_element_doesnt_bring_it_back()
	{
		var (host, _, _) = Page();
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		host.Tick(0.016, 0f, Surface);
		host.QueuePointer(PointerInput.Press(new Vector2(80f, 30f)));
		Rest(host, 0.3);
		host.QueuePointer(PointerInput.Release(new Vector2(80f, 30f)));
		Rest(host, 1.0);

		host.ToolTips.Showing.ShouldBeNull();
	}

	[Test]
	public void Leaving_or_a_key_hides_it()
	{
		var (host, _, root) = Page();
		host.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		Rest(host, 1.0);

		host.QueueKey(new KeyInput(VirtualKey.A, Down: true, Repeat: false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Surface);
		host.ToolTips.Showing.ShouldBeNull();
		root.Overlays.Count.ShouldBe(0);

		var (other, _, _) = Page();
		other.QueuePointer(PointerInput.Move(new Vector2(80f, 30f)));
		Rest(other, 1.0);
		other.QueuePointer(PointerInput.Move(new Vector2(300f, 200f)));
		other.Tick(0.016, 0f, Surface);
		other.ToolTips.Showing.ShouldBeNull();
	}

	[Test]
	public void An_element_inside_shows_the_tooltip_above_it()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		panel.Children.Add(new Border { Width = 100f, Height = 30f, Background = new Media.SolidColorBrush(HdrColor.FromRgb(255, 255, 255)) });
		ToolTipService.SetToolTip(panel, "The panel");
		view.Root.Content = panel;
		host.QueuePointer(PointerInput.Move(new Vector2(20f, 10f)));
		Rest(host, 1.0);

		host.ToolTips.Showing!.Content.ShouldBe("The panel");
	}

	[Test]
	public void Placement_stays_inside_the_bounds()
	{
		var bounds = new Size(400f, 300f);
		var size = new Size(120f, 30f);

		var nearRight = AnchoredPlacement.Place(new Rect(380f, 10f, 20f, 20f), size, bounds, PlacementMode.Bottom, 6f);
		(nearRight.X + size.Width).ShouldBeLessThanOrEqualTo(bounds.Width);

		var nearLeft = AnchoredPlacement.Place(new Rect(0f, 100f, 20f, 20f), size, bounds, PlacementMode.Left, 6f);
		nearLeft.X.ShouldBeGreaterThan(20f);   // turned to the right, where there is room
	}
}
