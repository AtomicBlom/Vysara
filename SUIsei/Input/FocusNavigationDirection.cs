namespace SUIsei.Input;

/// <summary>Direction of programmatic or keyboard-driven focus movement.</summary>
public enum FocusNavigationDirection
{
	/// <summary>Move to the next focusable element in tab order.</summary>
	Next,
	/// <summary>Move to the previous focusable element in tab order.</summary>
	Previous,
	Up,
	Down,
	Left,
	Right,
}
