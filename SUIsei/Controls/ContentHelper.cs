namespace SUIsei.Controls;

/// <summary>
/// Shared content-hosting policy (PlanningUI.md §D2.1): a <see cref="UIElement"/> is hosted as-is; a string (or any
/// other object) is wrapped in a <see cref="TextBlock"/>. Used by both <see cref="ContentControl"/> and
/// <see cref="ContentPresenter"/> so the rule lives in one place. (Data templates replace this in MU4.)
/// </summary>
internal static class ContentHelper
{
	public static UIElement? Wrap(object? value) => value switch
	{
		null => null,
		UIElement e => e,
		string s => new TextBlock { Text = s },
		_ => new TextBlock { Text = value.ToString() ?? string.Empty },
	};
}
