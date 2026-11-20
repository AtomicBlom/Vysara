using System.Numerics;

using SUIsei.Controls;
using SUIsei.Input;

namespace SUIsei.Tests;

/// <summary>
/// Drives a hosted page as a person would, for the control tests: host it, press keys, click (with any button) or wheel
/// over an element's middle, and find the parts of a built-in look.
/// </summary>
internal sealed class UiDriver
{
	public static readonly Vector2 Surface = new(500f, 400f);

	public UiDriver(UIElement content)
	{
		Host = new UIHost();
		Root = Host.CreateRoot(UILayer.Default).Root;
		Root.Content = content;
		Tick();
	}

	public UIHost Host { get; }

	public UIRoot Root { get; }

	/// <summary>Content laid out at its own size at the top left, as a page's controls usually are.</summary>
	public static StackPanel Top(params UIElement[] children)
	{
		var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		foreach (var child in children)
			panel.Children.Add(child);
		return panel;
	}

	public void Tick(double seconds = 0.016) => Host.Tick(seconds, 0f, Surface);

	public void Key(VirtualKey key, ModifierKeys modifiers = ModifierKeys.None)
	{
		Host.QueueKey(new KeyInput(key, Down: true, Repeat: false, modifiers, 0));
		Host.QueueKey(new KeyInput(key, Down: false, Repeat: false, modifiers, 0));
		Tick();
	}

	/// <summary>Where <paramref name="element"/> is drawn, in surface pixels.</summary>
	public static Rect Bounds(UIElement element) =>
		element.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, element.LayoutRect.Width, element.LayoutRect.Height));

	public static Vector2 Middle(UIElement element)
	{
		var at = Bounds(element);
		return new Vector2(at.X + at.Width / 2f, at.Y + at.Height / 2f);
	}

	public void Click(UIElement element, bool right = false) => Click(Middle(element), Middle(element), right);

	/// <summary>A press at <paramref name="from"/> released at <paramref name="to"/>, with the left or right button.</summary>
	public void Click(Vector2 from, Vector2 to, bool right = false)
	{
		var (press, release, held) = right
			? (PointerUpdateKind.RightButtonPressed, PointerUpdateKind.RightButtonReleased, PointerButtons.Right)
			: (PointerUpdateKind.LeftButtonPressed, PointerUpdateKind.LeftButtonReleased, PointerButtons.Left);
		Host.QueuePointer(PointerInput.Move(from));
		Host.QueuePointer(PointerInput.Press(from, press, held));
		if (from != to)
			Host.QueuePointer(PointerInput.Move(to, held));
		Host.QueuePointer(PointerInput.Release(to, release));
		Tick();
	}

	public void Wheel(UIElement element, Vector2 delta)
	{
		Host.QueuePointer(PointerInput.Scroll(Middle(element), delta));
		Tick();
	}

	/// <summary>Every <typeparamref name="T"/> under <paramref name="element"/>, depth first, not counting it.</summary>
	public static List<T> Parts<T>(UIElement element) where T : UIElement
	{
		var found = new List<T>();
		void Walk(UIElement e)
		{
			if (e is T t && !ReferenceEquals(e, element))
				found.Add(t);
			foreach (var child in e.VisualChildren)
				Walk(child);
		}
		Walk(element);
		return found;
	}
}
