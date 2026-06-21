namespace SUIsei;

/// <summary>Which layout/render stages a property change invalidates — ORed up the tree on a resolved-value change
/// (PlanningUI.md §D1.1). A pure-render change (a brush colour) sets only <see cref="Render"/>; a sizing change
/// sets <see cref="Measure"/>.</summary>
[Flags]
public enum InvalidationFlags { None = 0, Measure = 1, Arrange = 2, Render = 4 }
