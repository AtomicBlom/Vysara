using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.Controls.Primitives;

/// <summary>
/// The pointer and key handling a control that acts on a press takes from <see cref="ButtonBase"/> without being one (a
/// toggle switch, an expander's header): whether the pointer is over its <see cref="Area"/> and pressed on it, and a
/// primary press released over it, or Enter, Space or the pad's A while the control itself has focus, each acting once. A
/// right or middle press is left alone, for a context menu. The control shows <see cref="IsPointerOver"/> and
/// <see cref="IsPressed"/> in its <c>CommonStates</c>, told as they move.
/// </summary>
internal sealed class Pressable
{
	private readonly UIElement _owner;
	private readonly Action _act;
	private readonly Action _changed;
	private readonly RoutedEventHandler _entered;
	private readonly RoutedEventHandler _exited;
	private readonly RoutedEventHandler _pressed;
	private readonly RoutedEventHandler _released;

	/// <summary>Handles presses on <paramref name="owner"/>, or, with <paramref name="wholeControl"/> false, on the
	/// <see cref="Area"/> it is given later (a part of its template).</summary>
	public Pressable(UIElement owner, Action act, Action changed, bool wholeControl = true)
	{
		_owner = owner;
		_act = act;
		_changed = changed;
		_entered = (_, _) => Set(over: true, pressed: IsPressed);
		_exited = (_, _) => Set(over: false, pressed: false);
		_pressed = OnPressed;
		_released = OnReleased;
		owner.AddHandler(UIElement.KeyDownEvent, OnKeyDown);
		if (wholeControl)
			Area = owner;
	}

	/// <summary>What takes the presses: the control, or the part of it that acts (an expander's header row).</summary>
	public UIElement? Area
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			if (field is { } old)
			{
				old.RemoveHandler(UIElement.PointerEnteredEvent, _entered);
				old.RemoveHandler(UIElement.PointerExitedEvent, _exited);
				old.RemoveHandler(UIElement.PointerPressedEvent, _pressed);
				old.RemoveHandler(UIElement.PointerReleasedEvent, _released);
			}
			field = value;
			Set(over: false, pressed: false);
			if (value is not null)
			{
				value.AddHandler(UIElement.PointerEnteredEvent, _entered);
				value.AddHandler(UIElement.PointerExitedEvent, _exited);
				value.AddHandler(UIElement.PointerPressedEvent, _pressed);
				value.AddHandler(UIElement.PointerReleasedEvent, _released);
			}
		}
	}

	public bool IsPointerOver { get; private set; }

	public bool IsPressed { get; private set; }

	/// <summary>The control was disabled: a press or hover in progress can never end.</summary>
	public void Reset() => Set(over: false, pressed: false);

	/// <summary>The control's <c>CommonStates</c> state for these and whether it is enabled.</summary>
	public string State(bool enabled) => !enabled ? "Disabled" : IsPressed ? "Pressed" : IsPointerOver ? "PointerOver" : "Normal";

	private void OnPressed(object sender, RoutedEventArgs e)
	{
		if (e is PointerRoutedEventArgs { IsSecondaryButton: true })
			return;
		Set(IsPointerOver, pressed: true);
		e.Handled = true;
	}

	private void OnReleased(object sender, RoutedEventArgs e)
	{
		if (e is PointerRoutedEventArgs { IsSecondaryButton: true })
			return;
		var was = IsPressed;
		Set(IsPointerOver, pressed: false);
		if (was)
		{
			e.Handled = true;
			_act();
		}
	}

	// A key acts only while the control itself has focus, not something inside it (a text box in an expander's content)
	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is KeyRoutedEventArgs { Key: VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA }
			&& ReferenceEquals(e.OriginalSource, _owner))
		{
			_act();
			e.Handled = true;
		}
	}

	private void Set(bool over, bool pressed)
	{
		if (over == IsPointerOver && pressed == IsPressed)
			return;
		IsPointerOver = over;
		IsPressed = pressed;
		_changed();
	}
}
