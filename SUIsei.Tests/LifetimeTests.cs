using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Extensions;
using SUIsei.Input;
using SUIsei.Media;

namespace SUIsei.Tests;

/// <summary>
/// What an element keeps and gives up as it leaves a live tree and comes back: its transform and its shared geometry taken
/// up as they are now on return, and focus, its caret's blink, pointer capture and hover let go as it leaves, so a detached
/// element neither receives keys and drags nor keeps the host busy. The events that tell it so are raised once layout is
/// done, without allocating. And the group collections that would hold themselves refuse to, however their children are
/// given.
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
	/// Children assigned whole are checked as children added one at a time are, through the property and through
	/// <c>SetValue</c>, before the group listens to them: nothing changes when they are refused.
	/// </summary>
	[Test]
	public void A_geometry_group_refuses_children_assigned_whole_that_hold_it()
	{
		var group = new GeometryGroup();
		var holdsGroup = new GeometryCollection { group };
		Should.Throw<ArgumentException>(() => group.Children = holdsGroup);
		Should.Throw<ArgumentException>(() => group.SetValue(GeometryGroup.ChildrenProperty, holdsGroup));

		var outer = new GeometryGroup();
		outer.Children.Add(group);
		var holdsOuter = new GeometryCollection { new RectangleGeometry(), outer };
		Should.Throw<ArgumentException>(() => group.Children = holdsOuter);
		Should.Throw<ArgumentException>(() => group.SetValue(GeometryGroup.ChildrenProperty, holdsOuter));

		group.GetValue(GeometryGroup.ChildrenProperty).ShouldBeNull(); // refused before it was stored or listened to
		group.Children = [new RectangleGeometry()];
	}

	/// <summary>
	/// Groups shared down both sides of every level, 40 deep: walked naively that is 2⁴⁰ paths to the bottom. Each group is
	/// walked once, so adding them, and refusing the one that would close the loop, are quick.
	/// </summary>
	[Test]
	public void Groups_shared_down_many_branches_are_checked_once_each()
	{
		// Children made before the levels above listen: each change below notifies every path up through the diamond.
		var bottom = new GeometryGroup { Children = [new RectangleGeometry()] };
		var top = bottom;
		var transformBottom = new TransformGroup();
		var transformTop = transformBottom;
		for (var level = 0; level < 40; level++)
		{
			var next = new GeometryGroup();
			next.Children.Add(top);
			next.Children.Add(top);
			top = next;
			var nextTransform = new TransformGroup();
			nextTransform.Children.Add(transformTop);
			nextTransform.Children.Add(transformTop);
			transformTop = nextTransform;
		}

		Should.Throw<ArgumentException>(() => bottom.Children.Add(top));
		Should.Throw<ArgumentException>(() => bottom.Children = [top]);
		Should.Throw<ArgumentException>(() => transformBottom.Children.Add(transformTop));
		new GeometryGroup().Children.Add(top);
		new TransformGroup().Children.Add(transformTop);
	}

	/// <summary>
	/// A geometry shared with a path, changed while the path was out of the tree, sizes the path as it is now when it
	/// returns: the path heard nothing while it was away, and a layout pass finds it where it was.
	/// </summary>
	[Test]
	public void A_path_re_added_is_measured_from_its_shared_geometry_as_it_is_now()
	{
		var host = new UIHost();
		var shared = new RectangleGeometry { Rect = new Rect(0f, 0f, 20f, 10f) };
		var path = new Shapes.Path { Data = shared, Fill = new SolidColorBrush(new HdrColor(1f, 0f, 0f, 1f)), HorizontalAlignment = HorizontalAlignment.Left };
		var panel = new StackPanel();
		panel.Children.Add(path);
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, Surface);
		path.DesiredSize.ShouldBe(new Size(20f, 10f));

		panel.Children.RemoveAt(0);
		shared.Rect = new Rect(0f, 0f, 60f, 30f);
		panel.Children.Add(path);
		host.Tick(0.016, 0.016f, Surface);

		path.DesiredSize.ShouldBe(new Size(60f, 30f));
		path.LayoutRect.Width.ShouldBe(60f);
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

	/// <summary>
	/// A hovered button that leaves hears PointerExited, as a move off it would raise, so it doesn't come back showing
	/// PointerOver away from the pointer.
	/// </summary>
	[Test]
	public void A_hovered_button_that_leaves_is_exited_and_comes_back_unhovered()
	{
		var host = new UIHost();
		var button = new Button { Width = 40f, Height = 20f };
		var panel = new StackPanel();
		panel.Children.Add(button);
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, Surface);
		host.QueuePointer(PointerInput.Move(new Vector2(button.LayoutRect.X + 20f, button.LayoutRect.Y + 10f)));
		host.Tick(0.016, 0.016f, Surface);
		CommonState(button).ShouldBe("PointerOver");
		var exited = 0;
		button.AddHandler(UIElement.PointerExitedEvent, (_, _) => exited++);

		panel.Children.RemoveAt(0);
		host.Tick(0.016, 0.032f, Surface);

		exited.ShouldBe(1);
		CommonState(button).ShouldBe("Normal");

		panel.Children.Add(new Border { Width = 40f, Height = 20f });
		panel.Children.Add(button);
		host.Tick(0.016, 0.048f, Surface);
		CommonState(button).ShouldBe("Normal");
		exited.ShouldBe(1);
	}

	/// <summary>
	/// A PointerCaptureLost handler that changes another pointer's capture, while both captees are leaving: each captee
	/// hears it once, and the capture the handler made stands.
	/// </summary>
	[Test]
	public void A_capture_lost_handler_may_change_another_pointers_capture()
	{
		var host = new UIHost();
		var first = new Border { Width = 50, Height = 20 };
		var second = new Border { Width = 50, Height = 20 };
		var stays = new Border { Width = 50, Height = 20 };
		var leaving = new StackPanel();
		leaving.Children.Add(first);
		leaving.Children.Add(second);
		var panel = new StackPanel();
		panel.Children.Add(leaving);
		panel.Children.Add(stays);
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, Surface);
		first.CapturePointer(1).ShouldBeTrue();
		second.CapturePointer(2).ShouldBeTrue();
		var lost = new List<UIElement>();
		first.AddHandler(UIElement.PointerCaptureLostEvent, (_, _) =>
		{
			lost.Add(first);
			stays.CapturePointer(2).ShouldBeTrue();
			stays.CapturePointer(1).ShouldBeTrue();
		});
		second.AddHandler(UIElement.PointerCaptureLostEvent, (_, _) => lost.Add(second));

		panel.Children.RemoveAt(0);
		host.Tick(0.016, 0.016f, Surface);

		lost.ShouldBe([first, second]);
		host.Input.GetCapture(1).ShouldBeSameAs(stays);
		host.Input.GetCapture(2).ShouldBeSameAs(stays);
	}

	/// <summary>
	/// A focused row of a virtualized list scrolled out of view loses focus as the panel recycles it during measure. Its
	/// LostFocus handler replaces the list's items, which drops every realized row: it runs once the layout pass is done,
	/// not inside the panel's recycling loop, which it would leave walking rows that are gone.
	/// </summary>
	[Test]
	public void A_focused_row_scrolled_away_hears_LostFocus_after_layout_and_may_reset_the_list()
	{
		var host = new UIHost();
		var list = new ListView
		{
			Height = 100f,
			ItemsPanel = new ItemsPanelTemplate
			{
				Factory = static () =>
				{
					var rows = new VirtualizingStackPanel();
					Virtualization.SetRowHeight(rows, 20f);
					return rows;
				},
			},
			ItemsSource = Enumerable.Range(0, 100).Select(static i => $"row {i}").ToList(),
		};
		host.CreateRoot(UILayer.Default).Root.Content = list;
		host.Tick(0.016, 0f, Surface);
		var panel = (VirtualizingStackPanel)list.ItemsHost!;
		var row = panel.VisualChildren[0];
		host.Focus.TrySetFocus(row, FocusState.Programmatic).ShouldBeTrue();
		var lost = 0;
		row.AddHandler(UIElement.LostFocusEvent, (_, _) =>
		{
			lost++;
			list.ItemsSource = Enumerable.Range(0, 50).Select(static i => $"new {i}").ToList();
		});

		panel.VerticalOffset = 60f; // rows 0 to 2 scroll out, the focused row first
		host.Tick(0.016, 0.016f, Surface);

		lost.ShouldBe(1);
		host.Focus.FocusedElement.ShouldBeNull();
		host.Tick(0.016, 0.032f, Surface);
		panel.VisualChildren.Count.ShouldBe(6);
		panel.ExtentHeight.ShouldBe(50 * 20f);
	}

	/// <summary>
	/// Detaching hovered and captured elements under a host allocates nothing once warm: a virtualized list scrolled under
	/// a pointer recycles the row the pointer is over, the row holding another pointer's capture and the row a third
	/// pressed on, every few frames.
	/// </summary>
	[Test]
	[NotInParallel(TestIsolation.HotReload)]
	public void Recycling_hovered_and_captured_rows_under_a_host_allocates_nothing_once_warm()
	{
		var host = new UIHost();
		var items = new ItemsControl
		{
			Height = 200f,
			ItemsPanel = new ItemsPanelTemplate
			{
				Factory = static () =>
				{
					var rows = new VirtualizingStackPanel();
					Virtualization.SetRowHeight(rows, 20f);
					return rows;
				},
			},
			ItemsSource = Enumerable.Range(0, 100_000).Select(static i => $"Row {i}").ToList(),
		};
		host.CreateRoot(UILayer.Default).Root.Content = items;
		host.Tick(0.016, 0f, Surface);
		var panel = (VirtualizingStackPanel)items.ItemsHost!;
		var offset = 0f;
		void Frames(int count)
		{
			for (var f = 0; f < count; f++)
			{
				offset += 8f; // a row recycled every few frames, and another realized from the pool
				panel.VerticalOffset = offset;
				panel.VisualChildren[0].CapturePointer(2);
				host.QueuePointer(PointerInput.Move(new Vector2(30f, 5f), pointerId: 1));
				host.QueuePointer(PointerInput.Press(new Vector2(30f, 5f), pointerId: 3));
				host.Tick(0.016, 0f, Surface);
			}
		}

		Frames(30);
		items.RecycleCount.ShouldBeGreaterThan(0);
		host.Input.GetCapture(3)!.IsWithin(panel).ShouldBeTrue(); // pressed on a row
		host.WantsPointer.ShouldBeTrue();
		Allocations.ShouldAllocateNothing(() => Frames(60));
	}

	private static string? CommonState(Control control) =>
		VisualStateManager.GetVisualStateGroups(control.GetTemplateRoot()!)
			.Single(g => g.Name == "CommonStates").CurrentState?.Name;
}
