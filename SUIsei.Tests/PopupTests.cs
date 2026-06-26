using System.Numerics;

using SUIsei.Controls;

namespace SUIsei.Tests;

/// <summary>
/// <see cref="Popup"/> (PlanningUI.md MU6): opening hosts the child (and a light-dismiss layer) in the root overlay,
/// closing removes them, and an outside press light-dismisses while a press on the child does not.
/// </summary>
public sealed class PopupTests
{
	private static (UIHost Host, UIView View, Popup Popup) Build()
	{
		var host = new UIHost();
		var view = host.CreateRoot(UILayer.Default);
		var popup = new Popup
		{
			Child = new Border { Width = 50f, Height = 30f, Background = HdrColor.FromRgb(10, 10, 10) },
			HorizontalOffset = 100f,
			VerticalOffset = 100f,
		};
		var panel = new StackPanel();
		panel.Children.Add(popup);
		view.Root.Content = panel;
		host.Tick(0.016, 0f, new Vector2(300f, 300f));
		return (host, view, popup);
	}

	[Test]
	public void Opening_and_closing_add_and_remove_overlays()
	{
		var (host, view, popup) = Build();

		popup.IsOpen = true;
		view.Root.Overlays.Count.ShouldBe(2);   // light-dismiss layer + child

		popup.IsOpen = false;
		view.Root.Overlays.Count.ShouldBe(0);
	}

	[Test]
	public void Outside_press_light_dismisses_but_a_press_on_the_child_does_not()
	{
		var (host, view, popup) = Build();
		popup.IsOpen = true;
		host.Tick(0.016, 0f, new Vector2(300f, 300f));   // lay out the overlays

		// A press inside the child (100,100..150,130) keeps it open.
		host.QueuePointer(PointerInput.Press(new Vector2(120f, 110f)));
		host.QueuePointer(PointerInput.Release(new Vector2(120f, 110f)));
		host.Tick(0.016, 0f, new Vector2(300f, 300f));
		popup.IsOpen.ShouldBeTrue();

		// A press outside it lands on the dismiss layer and closes it.
		host.QueuePointer(PointerInput.Press(new Vector2(10f, 10f)));
		host.Tick(0.016, 0f, new Vector2(300f, 300f));
		popup.IsOpen.ShouldBeFalse();
	}
}
