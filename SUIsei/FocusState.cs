namespace SUIsei;

/// <summary>How focus was most recently acquired on an element (WinUI-compatible).</summary>
public enum FocusState
{
	/// <summary>The element does not have focus.</summary>
	Unfocused,
	/// <summary>Focus was acquired via pointer interaction.</summary>
	Pointer,
	/// <summary>Focus was acquired via Tab/arrow key navigation.</summary>
	Keyboard,
	/// <summary>Focus was acquired via a programmatic call to <see cref="FocusManager.TrySetFocus"/>.</summary>
	Programmatic,
}
