namespace Vysara.Controls;

/// <summary>Where a <see cref="NumberBox"/>'s up and down buttons show, as WinUI's
/// <c>NumberBoxSpinButtonPlacementMode</c>: not at all (the keys and wheel still step it), or inside the box at its right.
/// <see cref="Compact"/> shows them as <see cref="Inline"/> does.</summary>
public enum NumberBoxSpinButtonPlacementMode
{
	Hidden,
	Compact,
	Inline,
}
