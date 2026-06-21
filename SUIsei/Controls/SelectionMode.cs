namespace SUIsei.Controls;

/// <summary>How many items a <see cref="ListView"/> allows selected at once (PlanningUI.md §D1.6).</summary>
public enum SelectionMode
{
	/// <summary>Selection is disabled.</summary>
	None,

	/// <summary>At most one item is selected.</summary>
	Single,

	/// <summary>Multiple items may be selected (full multi-select is a later refinement).</summary>
	Multiple,
}
