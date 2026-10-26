using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// The container a <see cref="ListView"/> wraps each item in. A <c>SelectionStates</c>
/// <see cref="VisualStateManager"/> group shows a selection highlight behind the content, holding the item's background
/// at the selection brush while it is selected, faded in and out; the owning list calls <see cref="SetSelected"/> when the
/// selection changes. Unselected, the item paints its own <see cref="Control.Background"/>.
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
		var selection = new VisualStateGroup { Name = "SelectionStates" };
		selection.States.Add(new VisualState { Name = "Unselected", Storyboard = TemplateStates.RestoreBackground(this) });
		selection.States.Add(new VisualState { Name = "Selected", Storyboard = TemplateStates.Background(this, SelectedBackground) });
		VisualStateManager.SetVisualStateGroups(this, [selection]);
	}

	internal void SetSelected(bool selected)
	{
		if (IsSelected == selected)
			return;
		IsSelected = selected;
		VisualStateManager.GoToState(this, selected ? "Selected" : "Unselected", useTransitions: true);
	}
}
