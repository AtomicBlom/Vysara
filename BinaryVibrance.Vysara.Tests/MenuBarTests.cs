using System.Numerics;

using BinaryVibrance.Vysara.Controls;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <see cref="MenuBar"/> / <see cref="MenuBarItem"/>: items lay out left to right, invoking an
/// item opens its drop-down in the root overlay, invoking a drop-down entry fires and closes it, and the bar keeps
/// at most one item's menu open at a time.
/// </summary>
public sealed class MenuBarTests
{
	private static readonly Vector2 Framebuffer = new(400f, 300f);

	private static (UIHost Host, UIView View, MenuBar Bar) Build(out MenuBarItem file, out MenuBarItem edit)
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var bar = new MenuBar { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

		file = new MenuBarItem { Title = "File" };
		file.Items.Add(new MenuFlyoutItem { Text = "New" });
		file.Items.Add(new MenuFlyoutItem { Text = "Open" });
		edit = new MenuBarItem { Title = "Edit" };
		edit.Items.Add(new MenuFlyoutItem { Text = "Undo" });

		bar.Children.Add(file);
		bar.Children.Add(edit);
		view.Root.Content = bar;
		host.Tick(0.016, 0f, Framebuffer);
		return (host, view, bar);
	}

	[Test]
	public void Items_lay_out_left_to_right()
	{
		Build(out var file, out var edit);

		file.LayoutRect.Width.ShouldBeGreaterThan(0f);
		edit.LayoutRect.X.ShouldBe(file.LayoutRect.X + file.LayoutRect.Width, 0.5f);
	}

	[Test]
	public void Clicking_an_item_opens_its_drop_down()
	{
		var (_, view, bar) = Build(out var file, out _);

		file.PerformClick();

		file.IsSubmenuOpen.ShouldBeTrue();
		bar.IsAnyMenuOpen.ShouldBeTrue();
		view.Root.Overlays.Count.ShouldBe(2); // light-dismiss layer + menu
	}

	[Test]
	public void Invoking_a_drop_down_entry_fires_and_closes()
	{
		var (host, view, bar) = Build(out var file, out _);
		var invoked = 0;
		var save = new MenuFlyoutItem { Text = "Save" };
		save.Click += (_, _) => invoked++;
		file.Items.Add(save);

		file.PerformClick();
		host.Tick(0.016, 0f, Framebuffer);
		save.PerformClick();

		invoked.ShouldBe(1);
		file.IsSubmenuOpen.ShouldBeFalse();
		bar.IsAnyMenuOpen.ShouldBeFalse();
		view.Root.Overlays.Count.ShouldBe(0);
	}

	[Test]
	public void Opening_a_second_item_closes_the_first()
	{
		var (_, _, bar) = Build(out var file, out var edit);

		file.OpenMenu();
		file.IsSubmenuOpen.ShouldBeTrue();

		edit.OpenMenu();

		edit.IsSubmenuOpen.ShouldBeTrue();
		file.IsSubmenuOpen.ShouldBeFalse(); // the bar closed the previously-open item
		bar.IsAnyMenuOpen.ShouldBeTrue();
	}
}
