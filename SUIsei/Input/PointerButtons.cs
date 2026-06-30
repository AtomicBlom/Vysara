namespace SUIsei;

/// <summary>The set of pointer buttons currently held.</summary>
[Flags]
public enum PointerButtons : byte
{
	None = 0,
	Left = 1,
	Right = 2,
	Middle = 4,
	XButton1 = 8,
	XButton2 = 16,
}
