using System.Numerics;

using Vysara.Controls;
using Vysara.Controls.Primitives;
using Vysara.Input;

namespace Vysara.Tests;

/// <summary>
/// <see cref="Flyout"/> and <see cref="FlyoutBase"/>: shown against a target on its side, turned where there is no room,
/// focusing what is inside and giving focus back as Escape closes it; opened by <see cref="Button.Flyout"/> and, at the
/// pointer, by <see cref="UIElement.ContextFlyout"/> on a right-click or Shift+F10.
/// </summary>
public sealed class FlyoutTests
{
	private static readonly Vector2 Surface = new(400f, 300f);

	private static (UIHost Host, Button Target) Page(float top)
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var target = new Button
		{
			Width = 80f,
			Height = 30f,
			Margin = new Thickness(100f, top, 0f, 0f),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
		};
		view.Root.Content = target;
		host.Tick(0.016, 0f, Surface);
		return (host, target);
	}

	private static Flyout Holding(out Button inside)
	{
		inside = new Button { Content = "Inside" };
		return new Flyout { Content = inside, Placement = FlyoutPlacementMode.Bottom };
	}

	private static Rect Shown(UIElement element) => element.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, element.LayoutRect.Width, element.LayoutRect.Height));

	[Test]
	public void It_opens_on_its_side_and_turns_where_there_is_no_room()
	{
		var (_, target) = Page(top: 20f);
		var flyout = Holding(out var inside);
		flyout.ShowAt(target);
		Shown(inside).Y.ShouldBeGreaterThan(Shown(target).Bottom);

		var (_, low) = Page(top: 260f);
		var turned = Holding(out var above);
		turned.ShowAt(low);
		Shown(above).Bottom.ShouldBeLessThan(Shown(low).Y);
	}

	[Test]
	public void It_focuses_what_is_inside_and_Escape_gives_focus_back()
	{
		var (host, target) = Page(top: 20f);
		target.Focus(FocusState.Keyboard);
		var flyout = Holding(out var inside);

		flyout.ShowAt(target);
		host.Focus.FocusedElement.ShouldBe(inside);

		host.QueueKey(new KeyInput(VirtualKey.Escape, Down: true, Repeat: false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, Surface);
		flyout.IsOpen.ShouldBeFalse();
		host.Focus.FocusedElement.ShouldBe(target);
	}

	[Test]
	public void A_buttons_flyout_opens_as_it_is_clicked()
	{
		var (_, target) = Page(top: 20f);
		var flyout = Holding(out _);
		target.Flyout = flyout;

		target.PerformClick();

		flyout.IsOpen.ShouldBeTrue();
		flyout.Target.ShouldBe(target);
	}

	[Test]
	public void A_right_click_opens_the_context_flyout_at_the_pointer()
	{
		var (host, target) = Page(top: 20f);
		var menu = new MenuFlyout();
		menu.Items.Add(new MenuFlyoutItem { Text = "Delete" });
		target.ContextFlyout = menu;
		var point = new Vector2(150f, 30f);

		host.QueuePointer(PointerInput.Press(point, PointerUpdateKind.RightButtonPressed, PointerButtons.Right));
		host.QueuePointer(PointerInput.Release(point, PointerUpdateKind.RightButtonReleased, PointerButtons.None));
		host.Tick(0.016, 0f, Surface);

		menu.IsOpen.ShouldBeTrue();
		var item = menu.Items[0];
		Shown(item).X.ShouldBeGreaterThanOrEqualTo(point.X);
		Shown(item).Y.ShouldBeGreaterThanOrEqualTo(point.Y);
	}

	[Test]
	public void Shift_F10_opens_the_focused_elements_context_flyout()
	{
		var (host, target) = Page(top: 20f);
		var menu = new MenuFlyout();
		menu.Items.Add(new MenuFlyoutItem { Text = "Rename" });
		target.ContextFlyout = menu;
		target.Focus(FocusState.Keyboard);

		host.QueueKey(new KeyInput(VirtualKey.F10, Down: true, Repeat: false, ModifierKeys.Shift, 0));
		host.Tick(0.016, 0f, Surface);

		menu.IsOpen.ShouldBeTrue();
	}

	[Test]
	public void Full_placement_is_refused() =>
		Should.Throw<NotSupportedException>(() =>
		{
			var (_, target) = Page(top: 20f);
			new Flyout { Content = new Border(), Placement = FlyoutPlacementMode.Full }.ShowAt(target);
		});
}
