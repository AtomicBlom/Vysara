using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// The container a <see cref="ListView"/> wraps each item in. A <c>SelectionStates</c>
/// <see cref="VisualStateManager"/> group shows a selection highlight behind the content, holding the item's background
/// at the selection brush while it is selected, faded in and out; the owning list calls <see cref="SetSelected"/> when the
/// selection changes. Unselected, the item paints its own <see cref="Control.Background"/>. While disabled, a
/// <c>CommonStates</c> <c>Disabled</c> state holds the item's <see cref="Control.Foreground"/>, which its content's text
/// inherits, at the disabled text brush, and a selected item shows <c>SelectedDisabled</c>, a highlight without the
/// accent, in place of <c>Selected</c>.
/// </summary>
public sealed class ListViewItem : ContentControl
{
	private static readonly SolidColorBrush SelectedBackground = Brush.Sealed(HdrColor.FromRgb(50, 90, 160));

	/// <summary>The data item this container represents (used by the list to map selection back to the source).</summary>
	internal object? DataItem { get; init; }

	/// <summary>Whether this item is currently selected.</summary>
	public bool IsSelected { get; private set; }

	public ListViewItem()
	{
		BackgroundTransition = TemplateStates.Fade(80);
		var common = new VisualStateGroup { Name = "CommonStates" };
		common.States.Add(new VisualState { Name = "Normal" });
		common.States.Add(new VisualState
		{
			Name = "Disabled",
			Storyboard = TemplateStates.Hold((this, ForegroundProperty, TemplateStates.DisabledForeground)),
		});
		var selection = new VisualStateGroup { Name = "SelectionStates" };
		selection.States.Add(new VisualState { Name = "Unselected" });
		selection.States.Add(new VisualState { Name = "Selected", Storyboard = TemplateStates.Background(this, SelectedBackground) });
		selection.States.Add(new VisualState
		{
			Name = "SelectedDisabled",
			Storyboard = TemplateStates.Background(this, TemplateStates.DisabledAccent),
		});
		SetBuiltInValue(VisualStateManager.VisualStateGroupsProperty, [common, selection]);
	}

	internal void SetSelected(bool selected)
	{
		if (IsSelected == selected)
			return;
		IsSelected = selected;
		VisualStateManager.GoToState(this, SelectionState, useTransitions: true);
	}

	private string SelectionState => !IsSelected ? "Unselected" : IsEnabled ? "Selected" : "SelectedDisabled";

	// The item carries its states itself rather than in a template, so it shows them here as its effective IsEnabled moves.
	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		GoToCommonState(useTransitions: true);
		VisualStateManager.GoToState(this, SelectionState, useTransitions: true);
	}
}
