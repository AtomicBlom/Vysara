using System.Numerics;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Extensions;
using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// The runtime's own <c>vysara:</c> extensions, the attached properties that hold what WinUI's surface has no member
/// for: <see cref="Clipping"/>, <see cref="Scrolling"/> and <see cref="Virtualization"/>. Each must do what the
/// control member it replaced did.
/// </summary>
public sealed class ExtensionsTests
{
	private static (Border Border, UIRoot Root) Laid(bool? clip)
	{
		var border = new Border { Width = 30f, Height = 20f };
		if (clip is { } value)
			Clipping.SetClipToBounds(border, value);
		var root = new UIRoot(UISurface.Hud) { Content = border };
		root.LayoutPass(new Size(100f, 100f));
		return (border, root);
	}

	[Test]
	public void ClipToBounds_clips_the_element_to_its_layout_bounds()
	{
		Laid(clip: true).Border.Visual.Clip.ShouldBe(new Rect(0f, 0f, 30f, 20f));
		Laid(clip: null).Border.Visual.Clip.ShouldBeNull();
	}

	[Test]
	public void Changing_ClipToBounds_after_layout_takes_effect_on_the_next_pass()
	{
		var (border, root) = Laid(clip: null);

		Clipping.SetClipToBounds(border, true);
		root.LayoutPass(new Size(100f, 100f));

		border.Visual.Clip.ShouldBe(new Rect(0f, 0f, 30f, 20f));
	}

	[Test]
	public void A_control_that_always_clips_still_clips_when_markup_says_false()
	{
		var viewer = new ScrollViewer { Width = 40f, Height = 40f, Content = new Border { Height = 200f } };
		Clipping.SetClipToBounds(viewer, false);
		new UIRoot(UISurface.Hud) { Content = viewer }.LayoutPass(new Size(100f, 100f));

		viewer.Visual.Clip.ShouldNotBeNull();
	}

	[Test]
	[Arguments(null, 48f)]
	[Arguments(10f, 10f)]
	public void One_wheel_notch_scrolls_by_the_wheel_scroll_amount(float? amount, float expected)
	{
		var content = new Border { Width = 80f, Height = 500f, Background = new SolidColorBrush(HdrColor.FromRgb(50, 50, 50)) };
		var viewer = new ScrollViewer { Content = content, Width = 80f, Height = 100f };
		if (amount is { } value)
			Scrolling.SetWheelScrollAmount(viewer, value);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = viewer;
		host.Tick(0.016, 0f, new Vector2(80f, 100f));

		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(40f, 50f),
			PointerUpdateKind.None, PointerButtons.None, 1f, new Vector2(0f, -1f), 0));
		host.Tick(0.016, 0f, new Vector2(80f, 100f));

		viewer.VerticalOffset.ShouldBe(expected);
	}

	[Test]
	public void Changing_the_row_height_re_measures_the_virtualizing_panel()
	{
		var ic = new ItemsControl
		{
			ItemsPanel = new ItemsPanelTemplate { Factory = static () => new VirtualizingStackPanel() },
			ItemsSource = Enumerable.Range(0, 100).Select(i => $"item{i}").ToArray(),
		};
		var root = new UIRoot(UISurface.Hud) { Content = ic };
		root.LayoutPass(new Size(200f, 100f));
		var panel = ic.ItemsHost.ShouldBeOfType<VirtualizingStackPanel>();
		panel.ExtentHeight.ShouldBe(2400f);   // the default 24 per row

		Virtualization.SetRowHeight(panel, 50f);
		root.LayoutPass(new Size(200f, 100f));

		panel.ExtentHeight.ShouldBe(5000f);
		panel.VisualChildren.Count.ShouldBe(3);   // two whole rows and the partial one below them
	}
}
