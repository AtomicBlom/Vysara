using System.Numerics;

using SUIsei.Controls;
using SUIsei.Input;

namespace SUIsei.Tests;

/// <summary>
/// Keyboard focus: <see cref="FocusManager.TrySetFocus"/> raises GettingFocus→GotFocus→LostFocus in the
/// correct tunnel/bubble order; Tab navigation sorts by <c>(TabIndex, document order)</c> and wraps; elements with
/// <c>IsTabStop=false</c> or <c>IsEnabled=false</c> are skipped; a pointer press transfers focus.
/// </summary>
public sealed class FocusTests
{
	private static UIHost MakeHost(out UIView view)
	{
		var host = new UIHost();
		view = host.CreateRoot(UILayer.Default);
		return host;
	}

	[Test]
	public void TrySetFocus_raises_GotFocus_on_new_and_LostFocus_on_old()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 50f, Height = 30f };
		var b = new Button { Width = 50f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		view.Root.Content = panel;

		var gotFocusOn  = new List<string>();
		var lostFocusOn = new List<string>();
		a.GotFocus  += (_, _) => gotFocusOn.Add("A");
		a.LostFocus += (_, _) => lostFocusOn.Add("A");
		b.GotFocus  += (_, _) => gotFocusOn.Add("B");
		b.LostFocus += (_, _) => lostFocusOn.Add("B");

		host.Focus.TrySetFocus(a, FocusState.Programmatic);
		host.Focus.TrySetFocus(b, FocusState.Programmatic);

		gotFocusOn.ShouldBe(new[] { "A", "B" });
		lostFocusOn.ShouldBe(new[] { "A" });
	}

	[Test]
	public void GettingFocus_cancel_prevents_transfer()
	{
		var host = MakeHost(out var view);
		var btn = new Button { Width = 50f, Height = 30f };
		view.Root.Content = btn;

		btn.AddHandler(UIElement.GettingFocusEvent, (_, e) =>
		{
			if (e is GettingFocusEventArgs g) g.Cancel = true;
		});

		var result = host.Focus.TrySetFocus(btn, FocusState.Programmatic);

		result.ShouldBeFalse();
		host.Focus.FocusedElement.ShouldBeNull();
	}

	[Test]
	public void Tab_advances_in_document_order_by_default()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 40f, Height = 30f };
		var b = new Button { Width = 40f, Height = 30f };
		var c = new Button { Width = 40f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		panel.Children.Add(c);
		view.Root.Content = panel;

		host.Focus.TrySetFocus(a, FocusState.Keyboard);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);
		host.Focus.FocusedElement.ShouldBe(b);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);
		host.Focus.FocusedElement.ShouldBe(c);
	}

	[Test]
	public void Tab_wraps_from_last_to_first()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 40f, Height = 30f };
		var b = new Button { Width = 40f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		view.Root.Content = panel;

		host.Focus.TrySetFocus(b, FocusState.Keyboard);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);
		host.Focus.FocusedElement.ShouldBe(a);
	}

	[Test]
	public void ShiftTab_moves_backward_and_wraps()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 40f, Height = 30f };
		var b = new Button { Width = 40f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		view.Root.Content = panel;

		host.Focus.TrySetFocus(a, FocusState.Keyboard);
		host.Focus.MoveFocus(FocusNavigationDirection.Previous, host.Views);
		host.Focus.FocusedElement.ShouldBe(b);
	}

	[Test]
	public void IsTabStop_false_is_skipped_by_tab_navigation()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 40f, Height = 30f };
		var b = new Button { Width = 40f, Height = 30f, IsTabStop = false };
		var c = new Button { Width = 40f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		panel.Children.Add(c);
		view.Root.Content = panel;

		host.Focus.TrySetFocus(a, FocusState.Keyboard);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);

		host.Focus.FocusedElement.ShouldBe(c);
	}

	[Test]
	public void IsEnabled_false_is_skipped_by_tab_and_TrySetFocus_rejects_it()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 40f, Height = 30f };
		var b = new Button { Width = 40f, Height = 30f, IsEnabled = false };
		var c = new Button { Width = 40f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		panel.Children.Add(c);
		view.Root.Content = panel;

		host.Focus.TrySetFocus(a, FocusState.Keyboard);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);

		host.Focus.FocusedElement.ShouldBe(c);
		host.Focus.TrySetFocus(b, FocusState.Programmatic).ShouldBeFalse();
	}

	[Test]
	public void TabIndex_controls_order_before_document_order()
	{
		var host = MakeHost(out var view);
		var first  = new Button { Width = 40f, Height = 30f, TabIndex = 1 };
		var second = new Button { Width = 40f, Height = 30f, TabIndex = 2 };
		var third  = new Button { Width = 40f, Height = 30f, TabIndex = 3 };
		var panel  = new StackPanel { Orientation = Orientation.Horizontal };
		// Add in reverse tab-index order to verify sorting beats document order.
		panel.Children.Add(third);
		panel.Children.Add(first);
		panel.Children.Add(second);
		view.Root.Content = panel;

		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);
		host.Focus.FocusedElement.ShouldBe(first);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);
		host.Focus.FocusedElement.ShouldBe(second);
		host.Focus.MoveFocus(FocusNavigationDirection.Next, host.Views);
		host.Focus.FocusedElement.ShouldBe(third);
	}

	[Test]
	public void Pointer_press_on_a_focusable_element_sets_focus()
	{
		var host = MakeHost(out var view);
		var btn = new Button { Width = 100f, Height = 40f };
		view.Root.Content = btn;

		host.QueuePointer(PointerInput.Press(new Vector2(100f, 100f)));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		host.Focus.FocusedElement.ShouldBe(btn);
		host.Focus.FocusState.ShouldBe(FocusState.Pointer);
	}

	[Test]
	public void Tab_key_through_host_advances_focus()
	{
		var host = MakeHost(out var view);
		var a = new Button { Width = 40f, Height = 30f };
		var b = new Button { Width = 40f, Height = 30f };
		var panel = new StackPanel { Orientation = Orientation.Horizontal };
		panel.Children.Add(a);
		panel.Children.Add(b);
		view.Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		host.Focus.TrySetFocus(a, FocusState.Keyboard);
		host.QueueKey(new KeyInput(VirtualKey.Tab, Down: true, Repeat: false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		host.Focus.FocusedElement.ShouldBe(b);
	}

	[Test]
	public void DumpState_reports_the_focused_element()
	{
		var host = MakeHost(out var view);
		var btn = new Button { Width = 50f, Height = 30f };
		view.Root.Content = btn;

		host.Focus.TrySetFocus(btn, FocusState.Programmatic);

		host.DumpState().ShouldContain("focus=Button");
	}
}
