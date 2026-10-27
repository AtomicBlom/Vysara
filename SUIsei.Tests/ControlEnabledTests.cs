using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="Control.IsEnabled"/> with WinUI's semantics: it is inherited down through child controls (and
/// <see cref="Control.IsEnabledChanged"/> fires wherever the effective value moves, including on attach and detach),
/// a disabled control takes no focus and handles no input on itself while input it sources still bubbles to its
/// ancestors and it still blocks what is behind it, and it shows its <c>Disabled</c> visual state where its template
/// has one. Plain elements have no IsEnabled.
/// </summary>
public sealed class ControlEnabledTests
{
	// An outer ContentControl hosting a panel of two buttons.
	private static (ContentControl Outer, StackPanel Panel, Button A, Button B) Nested()
	{
		var a = new Button { Width = 40f, Height = 20f };
		var b = new Button { Width = 40f, Height = 20f };
		var panel = new StackPanel();
		panel.Children.Add(a);
		panel.Children.Add(b);
		return (new ContentControl { Content = panel }, panel, a, b);
	}

	[Test]
	public void Disabling_a_control_disables_every_control_beneath_it_and_enabling_restores_them()
	{
		var (outer, _, a, b) = Nested();

		outer.IsEnabled = false;
		a.IsEnabled.ShouldBeFalse();
		b.IsEnabled.ShouldBeFalse();

		// A value set while disabled by an ancestor is kept, and takes effect once the ancestor is enabled again.
		a.IsEnabled = false;
		outer.IsEnabled = true;
		a.IsEnabled.ShouldBeFalse();
		b.IsEnabled.ShouldBeTrue();

		a.IsEnabled = true;
		a.IsEnabled.ShouldBeTrue();
	}

	[Test]
	public void IsEnabledChanged_fires_for_each_control_whose_effective_value_changes()
	{
		var (outer, _, a, b) = Nested();
		b.IsEnabled = false;
		var changes = new List<string>();
		outer.IsEnabledChanged += (_, e) => changes.Add($"outer {e.OldValue}->{e.NewValue}");
		a.IsEnabledChanged += (_, e) => changes.Add($"a {e.OldValue}->{e.NewValue}");
		b.IsEnabledChanged += (_, e) => changes.Add($"b {e.OldValue}->{e.NewValue}");

		outer.IsEnabled = false;
		outer.IsEnabled = true;

		// b was already disabled on its own, so the ancestor changes nothing for it.
		changes.ShouldBe(["outer True->False", "a True->False", "outer False->True", "a False->True"]);
	}

	[Test]
	public void Attaching_under_a_disabled_control_disables_and_moving_out_re_enables()
	{
		var (outer, panel, _, _) = Nested();
		outer.IsEnabled = false;
		var c = new Button();
		var changes = new List<bool>();
		c.IsEnabledChanged += (_, e) => changes.Add((bool)e.NewValue!);

		panel.Children.Add(c);
		c.IsEnabled.ShouldBeFalse();

		// Re-parented into an enabled tree.
		panel.Children.RemoveAt(panel.Children.Count - 1);
		var other = new StackPanel();
		other.Children.Add(c);
		c.IsEnabled.ShouldBeTrue();

		changes.ShouldBe([false, true]);
	}

	[Test]
	public void A_controls_template_parts_are_disabled_with_it()
	{
		var inner = new Button();
		var control = new ContentControl
		{
			Template = new ControlTemplate { Factory = _ => new Border { Child = inner } },
			IsEnabled = false,
		};
		new UIRoot(UISurface.Hud) { Content = control }.LayoutPass(new Size(100f, 100f));

		inner.IsEnabled.ShouldBeFalse();
		control.IsEnabled = true;
		inner.IsEnabled.ShouldBeTrue();
	}

	[Test]
	public void A_disabled_control_takes_no_focus_and_Tab_skips_it()
	{
		var host = new UIHost();
		var (outer, _, a, b) = Nested();
		var after = new Button { Width = 40f, Height = 20f };
		var root = new StackPanel();
		root.Children.Add(outer);
		root.Children.Add(after);
		host.CreateRoot(UILayer.Default).Root.Content = root;
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		outer.IsEnabled = false;
		host.Focus.TrySetFocus(a, FocusState.Programmatic).ShouldBeFalse();
		host.Focus.TrySetFocus(outer, FocusState.Programmatic).ShouldBeFalse();

		host.QueueKey(new KeyInput(VirtualKey.Tab, true, false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));
		host.Focus.FocusedElement.ShouldBeSameAs(after);

		outer.IsEnabled = true;
		host.Focus.TrySetFocus(b, FocusState.Programmatic).ShouldBeTrue();
	}

	[Test]
	public void A_focused_control_that_is_disabled_loses_focus_before_keys_reach_it()
	{
		var host = new UIHost();
		var button = new Button { Width = 40f, Height = 20f };
		var clicks = 0;
		button.Click += (_, _) => clicks++;
		host.CreateRoot(UILayer.Default).Root.Content = button;
		host.Tick(0.016, 0f, new Vector2(200f, 200f));
		host.Focus.TrySetFocus(button, FocusState.Keyboard).ShouldBeTrue();

		button.IsEnabled = false;
		host.QueueKey(new KeyInput(VirtualKey.Enter, true, false, ModifierKeys.None, 0));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		host.Focus.FocusedElement.ShouldBeNull();
		clicks.ShouldBe(0);
	}

	[Test]
	public void A_disabled_control_blocks_what_is_behind_it_handles_nothing_and_its_input_bubbles_on()
	{
		var behind = new Border { Width = 100f, Height = 50f, Background = new SolidColorBrush(HdrColor.FromRgb(20, 20, 20)) };
		var button = new Button { Width = 100f, Height = 50f, IsEnabled = false };
		var canvas = new Canvas { Width = 200f, Height = 100f };
		canvas.Children.Add(behind);
		canvas.Children.Add(button);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = canvas;
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		var behindPresses = 0;
		var buttonPresses = 0;
		var canvasPresses = 0;
		var clicks = 0;
		behind.AddHandler(UIElement.PointerPressedEvent, (_, _) => behindPresses++);
		button.AddHandler(UIElement.PointerPressedEvent, (_, _) => buttonPresses++, handledEventsToo: true);
		canvas.AddHandler(UIElement.PointerPressedEvent, (_, _) => canvasPresses++);
		button.Click += (_, _) => clicks++;

		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(50f, 25f),
			PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 1f, Vector2.Zero, 0));
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(50f, 25f),
			PointerUpdateKind.LeftButtonReleased, PointerButtons.None, 0f, Vector2.Zero, 0));
		host.Tick(0.016, 0f, new Vector2(200f, 100f));

		behindPresses.ShouldBe(0);   // still hit-testable: the disabled button covers the border behind it
		buttonPresses.ShouldBe(0);   // not even a handledEventsToo handler runs on the disabled control
		canvasPresses.ShouldBe(1);   // the press bubbles past it to an ancestor
		clicks.ShouldBe(0);
		host.Focus.FocusedElement.ShouldBeNull();

		button.IsEnabled = true;
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(50f, 25f),
			PointerUpdateKind.LeftButtonPressed, PointerButtons.Left, 1f, Vector2.Zero, 0));
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, new Vector2(50f, 25f),
			PointerUpdateKind.LeftButtonReleased, PointerButtons.None, 0f, Vector2.Zero, 0));
		host.Tick(0.016, 0f, new Vector2(200f, 100f));
		buttonPresses.ShouldBe(1);
		clicks.ShouldBe(1);
	}

	// A content control whose template has a CommonStates group with Normal and Disabled.
	private static (ContentControl Control, VisualStateGroup Common) WithDisabledState(bool enabled)
	{
		var common = new VisualStateGroup { Name = "CommonStates" };
		common.States.Add(new VisualState { Name = "Normal" });
		common.States.Add(new VisualState { Name = "Disabled" });
		var control = new ContentControl
		{
			Template = new ControlTemplate
			{
				Factory = _ =>
				{
					var border = new Border();
					VisualStateManager.SetVisualStateGroups(border, [common]);
					return border;
				},
			},
			IsEnabled = enabled,
		};
		new UIRoot(UISurface.Hud) { Content = control }.LayoutPass(new Size(100f, 100f));
		return (control, common);
	}

	[Test]
	public void A_disabled_control_shows_its_Disabled_state_and_returns_to_Normal()
	{
		var (control, common) = WithDisabledState(enabled: true);

		control.IsEnabled = false;
		common.CurrentState!.Name.ShouldBe("Disabled");
		control.IsEnabled = true;
		common.CurrentState!.Name.ShouldBe("Normal");
	}

	[Test]
	public void A_control_created_disabled_shows_Disabled_once_its_template_is_applied()
	{
		var (_, common) = WithDisabledState(enabled: false);

		common.CurrentState!.Name.ShouldBe("Disabled");
	}

	[Test]
	public void A_disabled_control_inherits_the_Disabled_state_from_its_ancestor()
	{
		var (control, common) = WithDisabledState(enabled: true);
		var outer = new ContentControl { Content = control };

		outer.IsEnabled = false;

		common.CurrentState!.Name.ShouldBe("Disabled");
	}

	[Test]
	public void The_Slider_shows_Disabled_and_a_template_without_one_falls_back_to_Normal()
	{
		var slider = new Slider { Width = 100f };
		var button = new Button { Width = 40f, Height = 20f };
		var panel = new StackPanel();
		panel.Children.Add(slider);
		panel.Children.Add(button);
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(200f, 200f));

		slider.IsEnabled = false;
		CommonState(slider).ShouldBe("Disabled");

		// Hover the button so it shows PointerOver; its template has no Disabled state.
		var buttonCentre = new Vector2(button.LayoutRect.X + 20f, button.LayoutRect.Y + 10f);
		host.QueuePointer(new PointerInput(0, PointerDeviceType.Mouse, buttonCentre,
			PointerUpdateKind.None, PointerButtons.None, 0f, Vector2.Zero, 0));
		host.Tick(0.016, 0f, new Vector2(200f, 200f));
		CommonState(button).ShouldBe("PointerOver");

		button.IsEnabled = false;
		CommonState(button).ShouldBe("Normal");
	}

	private static string? CommonState(Control control) =>
		VisualStateManager.GetVisualStateGroups(control.GetTemplateRoot()!)
			.Single(g => g.Name == "CommonStates").CurrentState?.Name;

	[Test]
	public void IsEnabled_is_a_control_property_in_markup()
	{
		const string codeBehind = "namespace Demo { public partial class V { } }";
		static string View(string body) => $$"""
			<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
			            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
			            x:Class="Demo.V">
			  {{body}}
			</StackPanel>
			""";

		var control = View("""<Button IsEnabled="False" />""");
		XamlGeneratorHarness.Run(control, codeBehind).Diagnostics.ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(control, codeBehind).ShouldBeEmpty();

		XamlGeneratorHarness.Run(View("""<Border IsEnabled="False" />"""), codeBehind).DiagnosticIds().ShouldBe(["SUISEI004"]);
	}
}
