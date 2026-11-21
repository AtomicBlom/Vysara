using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls.Primitives;

/// <summary>
/// The pointer and key handling a control that acts on a press takes from <see cref="ButtonBase"/> without being one (a
/// toggle switch, an expander's header): whether the pointer is over it and pressed on it, and the press released over
/// it, Enter, Space or the pad's A, each acting once. The control shows <see cref="IsPointerOver"/> and
/// <see cref="IsPressed"/> in its <c>CommonStates</c>; <c>changed</c> is told as they move. A control that
/// hosts other controls (an expander's content) passes <c>presses</c>, which says whether an element the
/// pointer is over is part of what presses it; and a key acts only when the control itself has focus, not something in it.
/// </summary>
internal sealed class Pressable
{
	private readonly Action _act;
	private readonly Action _changed;

	public Pressable(UIElement owner, Action act, Action changed, Func<object?, bool>? presses = null)
	{
		_act = act;
		_changed = changed;
		presses ??= static _ => true;
		owner.AddHandler(UIElement.PointerEnteredEvent, (_, e) => Set(over: presses(e.OriginalSource), pressed: IsPressed));
		owner.AddHandler(UIElement.PointerMovedEvent, (_, e) =>
		{
			var over = presses(e.OriginalSource);
			Set(over, pressed: IsPressed && over);
		});
		owner.AddHandler(UIElement.PointerExitedEvent, (_, _) => Set(over: false, pressed: false));
		owner.AddHandler(UIElement.PointerPressedEvent, (_, e) =>
		{
			if (!presses(e.OriginalSource))
				return;
			Set(over: true, pressed: true);
			e.Handled = true;
		});
		owner.AddHandler(UIElement.PointerReleasedEvent, (_, e) =>
		{
			var was = IsPressed;
			Set(IsPointerOver, pressed: false);
			if (was && presses(e.OriginalSource))
				_act();
		});
		owner.AddHandler(UIElement.KeyDownEvent, (_, e) =>
		{
			if (ReferenceEquals(e.OriginalSource, owner) && e is KeyRoutedEventArgs { Key: VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA })
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
