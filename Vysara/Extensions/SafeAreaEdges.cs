namespace Vysara.Extensions;

/// <summary>The edges of the surface an element keeps clear of where the screen hides content (see <see cref="SafeArea"/>).</summary>
[Flags]
public enum SafeAreaEdges
{
	/// <summary>None: the element is arranged where its parent puts it, under a cutout if that is where it lands.</summary>
	None = 0,

	Left = 1,
	Top = 2,
	Right = 4,
	Bottom = 8,

	/// <summary>Every edge.</summary>
	All = Left | Top | Right | Bottom,
}
