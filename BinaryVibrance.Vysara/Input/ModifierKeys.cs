namespace BinaryVibrance.Vysara.Input;

/// <summary>Modifier keys held at the time of a keyboard or pointer event.</summary>
[Flags]
public enum ModifierKeys
{
	None = 0,
	Control = 1,
	Alt = 2,
	Shift = 4,
	Windows = 8,
}
