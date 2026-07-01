using SUIsei.Animation;

namespace SUIsei.Controls;

/// <summary>
/// The container a <see cref="ListView"/> wraps each item in. A <c>SelectionStates</c>
/// <see cref="VisualStateManager"/> group animates a selection highlight behind the content; the owning list calls
/// <see cref="SetSelected"/> when the selection changes.
/// </summary>
public sealed class ListViewItem : ContentControl
{
	private static readonly HdrColor SelectedBackground = HdrColor.FromRgb(50, 90, 160);

	/// <summary>The data item this container represents (used by the list to map selection back to the source).</summary>
	internal object? DataItem { get; init; }

	/// <summary>Whether this item is currently selected.</summary>
	public bool IsSelected { get; private set; }

	public ListViewItem()
	{
		var selection = new VisualStateGroup { Name = "SelectionStates" };
		selection.States.Add(new VisualState { Name = "Unselected", Storyboard = HighlightTo(SelectedBackground, HdrColor.Transparent) });
		selection.States.Add(new VisualState { Name = "Selected", Storyboard = HighlightTo(HdrColor.Transparent, SelectedBackground) });
		VisualStateManager.SetVisualStateGroups(this, [selection]);
	}

	internal void SetSelected(bool selected)
	{
		if (IsSelected == selected)
			return;
		IsSelected = selected;
		VisualStateManager.GoToState(this, selected ? "Selected" : "Unselected", useTransitions: true);
	}

	private Storyboard HighlightTo(HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(80), FillBehavior = FillBehavior.HoldEnd,
				Apply = SetAnimatedFill, Release = ClearAnimatedFill,
			},
		},
	};
}
