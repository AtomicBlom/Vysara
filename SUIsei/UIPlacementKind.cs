namespace SUIsei;

/// <summary>Where a <see cref="UIView"/> renders (PlanningUI.md §D5.1). MU2 ships <see cref="Overlay"/>; in-world
/// quads (and their 3D ray hit-test) arrive in MU5.</summary>
public enum UIPlacementKind : byte { Overlay, InWorld }
