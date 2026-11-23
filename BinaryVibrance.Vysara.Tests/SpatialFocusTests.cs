using System.Numerics;

using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Input;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// A gamepad through a page: the d-pad's keys move focus to the nearest tab stop that way (straight ahead beating nearer
/// ones off to the side, never wrapping, within the focused element's view, an element's XYFocus overrides first), and
/// A presses a button as Enter does. The keys never reach the focused element.
/// </summary>
public sealed class SpatialFocusTests
{
	private static readonly Vector2 Surface = new(400f, 400f);

	// A 3×3 grid of buttons, 60×30 with 20 between, at (10,10)
	private static (UIHost Host, Button[,] Buttons) Grid3(out UIView view)
	{
		var host = new UIHost();
		view = host.CreateRoot(UILayer.Default);
		var canvas = new Canvas();
		var buttons = new Button[3, 3];
		for (int r = 0; r < 3; r++)
			for (int c = 0; c < 3; c++)
			{
				var b = new Button { Width = 60f, Height = 30f };
				Canvas.SetLeft(b, 10f + c * 80f);
				Canvas.SetTop(b, 10f + r * 50f);
				canvas.Children.Add(b);
				buttons[r, c] = b;
			}
		view.Root.Content = canvas;
		host.Tick(0.016, 0f, Surface);
		return (host, buttons);
	}

	private static void Press(UIHost host, VirtualKey key)
	{
		host.QueueKey(new KeyInput(key, Down: true, Repeat: false, ModifierKeys.None, 0));
		host.QueueKey(new KeyInput(key, Down: false, Repeat: false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Surface);
	}

	[Test]
	public void The_dpad_moves_focus_to_the_neighbour_that_way_and_stops_at_the_edge()
	{
		var (host, b) = Grid3(out _);
		Press(host, VirtualKey.GamepadDPadRight);
		host.Focus.FocusedElement.ShouldBe(b[0, 0]);   // nothing focused: the first tab stop

		Press(host, VirtualKey.GamepadDPadRight);
		host.Focus.FocusedElement.ShouldBe(b[0, 1]);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(b[1, 1]);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(b[2, 1]);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(b[2, 1]);   // the bottom: it doesn't wrap
		Press(host, VirtualKey.GamepadDPadLeft);
		host.Focus.FocusedElement.ShouldBe(b[2, 0]);
		Press(host, VirtualKey.GamepadDPadUp);
		host.Focus.FocusedElement.ShouldBe(b[1, 0]);
		host.Focus.FocusState.ShouldBe(FocusState.Keyboard);
	}

	[Test]
	public void Straight_ahead_beats_nearer_off_to_the_side()
	{
		var a = new Rect(0f, 0f, 50f, 20f);
		var ahead = new Rect(0f, 100f, 50f, 20f);    // 80 below, straight down
		var aside = new Rect(120f, 40f, 50f, 20f);   // 20 below, 70 to the side
		FocusManager.Score(a, ahead, FocusNavigationDirection.Down)!.Value
			.ShouldBeLessThan(FocusManager.Score(a, aside, FocusNavigationDirection.Down)!.Value);
		FocusManager.Score(a, ahead, FocusNavigationDirection.Up).ShouldBeNull();
		FocusManager.Score(a, aside, FocusNavigationDirection.Right).ShouldNotBeNull();
	}

	[Test]
	public void An_elements_XYFocus_override_wins()
	{
		var (host, b) = Grid3(out _);
		b[0, 0].XYFocusDown = b[2, 2];
		host.Focus.TrySetFocus(b[0, 0], FocusState.Keyboard);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(b[2, 2]);
	}

	[Test]
	public void Focus_stays_within_the_view_it_is_in()
	{
		var (host, b) = Grid3(out _);
		var menu = host.CreateRoot(UILayer.Default);
		var other = new Button { Width = 60f, Height = 30f };
		var canvas = new Canvas();
		Canvas.SetLeft(other, 10f);
		Canvas.SetTop(other, 300f);   // below the grid, in another view
		canvas.Children.Add(other);
		menu.Root.Content = canvas;
		host.Tick(0.016, 0f, Surface);

		host.Focus.TrySetFocus(b[2, 0], FocusState.Keyboard);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(b[2, 0]);
	}

	[Test]
	public void The_dpad_moves_down_a_stacked_column_of_content_controls()
	{
		// A menu's shape: a bordered panel holding a column of lines, each a content control round a border
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var lines = new StackPanel();
		var items = new ContentControl[3];
		for (int i = 0; i < 3; i++)
		{
			items[i] = new ContentControl { Content = new Border { Height = 30f, Child = new TextBlock { Text = $"line {i}" } } };
			lines.Children.Add(items[i]);
		}
		var column = new StackPanel();
		column.Children.Add(new TextBlock { Text = "Title" });
		column.Children.Add(lines);
		view.Root.Content = new Border { Width = 300f, HorizontalAlignment = HorizontalAlignment.Left, Child = column };
		host.Tick(0.016, 0f, Surface);

		host.Focus.TrySetFocus(items[0], FocusState.Keyboard);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(items[1]);
		Press(host, VirtualKey.GamepadDPadDown);
		host.Focus.FocusedElement.ShouldBe(items[2]);
	}

	[Test]
	public void A_presses_the_focused_button()
	{
		var (host, b) = Grid3(out _);
		int clicks = 0;
		b[0, 0].Click += (_, _) => clicks++;
		host.Focus.TrySetFocus(b[0, 0], FocusState.Keyboard);
		Press(host, VirtualKey.GamepadA);
		clicks.ShouldBe(1);
	}

	[Test]
	public void The_dpad_reaches_the_focused_element_first_and_moves_focus_only_if_it_doesnt_handle_it()
	{
		var (host, b) = Grid3(out _);
		int stepped = 0;
		// A value row: takes left and right itself
		b[1, 1].KeyDown += (_, e) =>
		{
			if (e is RoutedEvents.KeyRoutedEventArgs { Key: VirtualKey.GamepadDPadLeft or VirtualKey.GamepadDPadRight })
			{
				stepped++;
				e.Handled = true;
			}
		};
		host.Focus.TrySetFocus(b[1, 1], FocusState.Keyboard);
		Press(host, VirtualKey.GamepadDPadRight);
		stepped.ShouldBe(1);
		host.Focus.FocusedElement.ShouldBe(b[1, 1]);
		Press(host, VirtualKey.GamepadDPadDown);   // not handled: focus moves
		host.Focus.FocusedElement.ShouldBe(b[2, 1]);
	}
}
