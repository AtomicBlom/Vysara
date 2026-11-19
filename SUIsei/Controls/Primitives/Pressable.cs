using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls.Primitives;

/// <summary>
/// The pointer and key handling a control that acts on a press takes from <see cref="ButtonBase"/> without being one (a
/// toggle switch, an expander's header): whether the pointer is over it and pressed on it, and the press released over
/// it, Enter, Space or the pad's A, each acting once. The control shows <see cref="IsPointerOver"/> and
/// <see cref="IsPressed"/> in its <c>CommonStates</c>; <paramref name="changed"/> is told as they move.
/// </summary>
internal sealed class Pressable
{
	private readonly Action _act;
	private readonly Action _changed;

	public Pressable(UIElement owner, Action act, Action changed)
	{
		_act = act;
		_changed = changed;
		owner.AddHandler(UIElement.PointerEnteredEvent, (_, _) => Set(over: true, pressed: IsPressed));
		owner.AddHandler(UIElement.PointerExitedEvent, (_, _) => Set(over: false, pressed: false));
		owner.AddHandler(UIElement.PointerPressedEvent, (_, e) =>
		{
			Set(IsPointerOver, pressed: true);
			e.Handled = true;
		});
		owner.AddHandler(UIElement.PointerReleasedEvent, (_, _) =>
		{
			var was = IsPressed;
			Set(IsPointerOver, pressed: false);
			if (was)
				_act();
		});
		owner.AddHandler(UIElement.KeyDownEvent, (_, e) =>
		{
			if (e is KeyRoutedEventArgs { Key: VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA })
			{
				_act();
				e.Handled = true;
			}
		});
	}

	public bool IsPointerOver { get; private set; }

	public bool IsPressed { get; private set; }

	/// <summary>The control was disabled: a press or hover in progress can never end.</summary>
	public void Reset() => Set(over: false, pressed: false);

	/// <summary>The control's <c>CommonStates</c> state for these and whether it is enabled.</summary>
	public string State(bool enabled) => !enabled ? "Disabled" : IsPressed ? "Pressed" : IsPointerOver ? "PointerOver" : "Normal";

	private void Set(bool over, bool pressed)
	{
		if (over == IsPointerOver && pressed == IsPressed)
			return;
		IsPointerOver = over;
		IsPressed = pressed;
		_changed();
	}
}
