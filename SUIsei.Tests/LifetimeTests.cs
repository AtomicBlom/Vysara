using System.Numerics;
using SUIsei.Controls;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// What an element keeps and gives up as it leaves a live tree and comes back: its transform re-applied on return, and
/// focus, its caret's blink and pointer capture let go as it leaves, so a detached element neither receives keys and drags
/// nor keeps the host busy. And the group collections that would hold themselves refuse to.
/// </summary>
public sealed class LifetimeTests
{
	private static readonly Vector2 Surface = new(300f, 200f);

	/// <summary>
	/// A shared transform changed while the element was away is applied when it returns: the layout pass finds the
	/// element where it was and arranges nothing, so only re-applying on attach brings it in.
	/// </summary>
	[Test]
	public void An_element_re_added_takes_up_its_transform_as_it_is_now()
	{
		var host = new UIHost();
		var moving = new TranslateTransform { X = 10 };
		var spacer = new Border { Width = 90, Height = 10 };
		var border = new Border { Width = 20, Height = 20, RenderTransform = moving };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(spacer);
		panel.Children.Add(border);
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, Surface);
		border.Visual.Transform.M31.ShouldBe(100f);

		panel.Children.RemoveAt(1);
		moving.X = 80;
		panel.Children.Add(border);
		host.Tick(0.016, 0.016f, Surface);
		host.Render();

		border.Visual.Transform.M31.ShouldBe(170f);
	}

	[Test]
	public void A_group_that_would_hold_itself_refuses()
	{
		var group = new TransformGroup();
		Should.Throw<ArgumentException>(() => group.Children.Add(group));
		var outer = new TransformGroup();
		outer.Children.Add(group);
		Should.Throw<ArgumentException>(() => group.Children.Add(outer));

		var geometry = new GeometryGroup();
		Should.Throw<ArgumentException>(() => geometry.Children.Add(geometry));
		var holder = new GeometryGroup();
		holder.Children.Add(geometry);
		Should.Throw<ArgumentException>(() => geometry.Children.Add(holder));
	}

	/// <summary>
	/// Removing (or rebuilding) a view with a focused text box: focus goes, so keys stop going to the detached box and the
	/// host no longer wants the keyboard, and the caret's endless blink stops on the clock it ran on.
	/// </summary>
	[Test]
	public void A_focused_text_box_that_leaves_gives_up_focus_and_stops_its_caret()
	{
		var host = new UIHost();
		var box = new TextBox { Text = "typed", Width = 100 };
		var panel = new StackPanel();
		panel.Children.Add(box);
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, Surface);
		host.Focus.TrySetFocus(box, FocusState.Programmatic).ShouldBeTrue();
		host.Tick(0.016, 0.016f, Surface);
		host.Clock.HasActiveClocks.ShouldBeTrue(); // the caret blinks

		panel.Children.RemoveAt(0);
		host.Tick(0.016, 0.032f, Surface);

		host.Focus.FocusedElement.ShouldBeNull();
		host.WantsKeyboard.ShouldBeFalse();
		host.Clock.HasActiveClocks.ShouldBeFalse();
	}

	[Test]
	public void An_element_holding_pointer_capture_that_leaves_releases_it()
	{
		var host = new UIHost();
		var dragged = new Border { Width = 50, Height = 50, Background = new SolidColorBrush(new HdrColor(1f, 0f, 0f, 1f)) };
		var panel = new StackPanel();
		panel.Children.Add(dragged);
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, Surface);
		var lost = 0;
		dragged.AddHandler(UIElement.PointerCaptureLostEvent, (_, _) => lost++);
		dragged.CapturePointer(1).ShouldBeTrue();

		panel.Children.RemoveAt(0);
		host.Tick(0.016, 0.016f, Surface);

		lost.ShouldBe(1);
		host.WantsPointer.ShouldBeFalse();
	}
}
