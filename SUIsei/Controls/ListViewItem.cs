using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// The container a <see cref="ListView"/> wraps each item in: its content laid out across the row, left-aligned, inside
/// its <see cref="Control.Padding"/>. A <c>SelectionStates</c> <see cref="VisualStateManager"/> group shows a highlight
/// behind the content as the pointer passes over it and while it is selected, holding the item's background at the
/// highlight's brush, faded in and out; the owning list calls <see cref="SetSelected"/> when the selection changes.
/// Unselected and not hovered, the item paints its own <see cref="Control.Background"/>: clear unless set, but the whole
/// row takes the pointer. A page recolours the highlights
/// with WinUI's <c>ListViewItemBackgroundPointerOver</c>, <c>ListViewItemBackgroundSelected</c>,
/// <c>ListViewItemBackgroundSelectedPointerOver</c> and <c>ListViewItemForegroundSelected</c> resources, read as the item
/// joins a tree. While disabled, a <c>CommonStates</c> <c>Disabled</c> state holds the item's
/// <see cref="Control.Foreground"/>, which its content's text inherits, at the disabled text brush, and a selected item
/// shows <c>SelectedDisabled</c>, a highlight without the accent. An item whose content is collapsed takes no room, so a
/// list filtered by hiding its items' content closes up.
/// </summary>
public sealed class ListViewItem : ContentControl
{
	private static readonly SolidColorBrush SelectedBackground = Brush.Sealed(HdrColor.FromRgb(50, 90, 160));
	private static readonly SolidColorBrush OverBackground = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 18));
	private static readonly SolidColorBrush SelectedOverBackground = Brush.Sealed(HdrColor.FromRgb(62, 104, 178));
	// Clear, but drawn, so the whole row takes the pointer and not only its text
	private static readonly SolidColorBrush RowBackground = Brush.Sealed(HdrColor.Transparent);
	private static readonly Thickness BuiltInPadding = new(10f, 5f, 10f, 5f);

	private bool _isPointerOver;
	// The resources the highlights were last built from, so joining a tree rebuilds them only when they differ.
	private (Brush? Over, Brush? Selected, Brush? SelectedOver, Brush? SelectedText) _built;

	/// <summary>The data item this container represents (used by the list to map selection back to the source).</summary>
	internal object? DataItem { get; init; }

	/// <summary>Whether this item is currently selected.</summary>
	public bool IsSelected { get; private set; }

	/// <summary>Whether the item shows anything: its content is not collapsed.</summary>
	internal bool ShowsContent => Content is not UIElement { Visibility: Visibility.Collapsed };

	public ListViewItem()
	{
		BackgroundTransition = TemplateStates.Fade(80);
		SetBuiltInValue(PaddingProperty, BuiltInPadding);
		SetBuiltInValue(BackgroundProperty, RowBackground);
		BuildStates(null, null, null, null);
		AddHandler(PointerEnteredEvent, (_, _) => Hover(true));
		AddHandler(PointerExitedEvent, (_, _) => Hover(false));
	}

	internal void SetSelected(bool selected)
	{
		if (IsSelected == selected)
			return;
		IsSelected = selected;
		VisualStateManager.GoToState(this, SelectionState, useTransitions: true);
	}

	private string SelectionState => !IsEnabled
		? IsSelected ? "SelectedDisabled" : "Unselected"
		: IsSelected ? _isPointerOver ? "SelectedPointerOver" : "Selected"
		: _isPointerOver ? "UnselectedPointerOver" : "Unselected";

	private void Hover(bool over)
	{
		_isPointerOver = over;
		VisualStateManager.GoToState(this, SelectionState, useTransitions: true);
	}

	// The item carries its states itself rather than in a template, so it shows them here as its effective IsEnabled moves.
	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		_isPointerOver = false;
		GoToCommonState(useTransitions: true);
		VisualStateManager.GoToState(this, SelectionState, useTransitions: true);
	}

	// Joined a tree: the highlights read the page's resources, rebuilt only where one differs from what they were built from
	private protected override void OnLiveTreeChanged(bool attached)
	{
		base.OnLiveTreeChanged(attached);
		if (!attached)
			return;
		var found = (FindResource("ListViewItemBackgroundPointerOver") as Brush, FindResource("ListViewItemBackgroundSelected") as Brush,
			FindResource("ListViewItemBackgroundSelectedPointerOver") as Brush, FindResource("ListViewItemForegroundSelected") as Brush);
		if (found == _built)
			return;
		BuildStates(found.Item1, found.Item2, found.Item3, found.Item4);
		VisualStateManager.GoToState(this, SelectionState, useTransitions: false);
	}

	private void BuildStates(Brush? over, Brush? selected, Brush? selectedOver, Brush? selectedText)
	{
		_built = (over, selected, selectedOver, selectedText);
		var common = new VisualStateGroup { Name = "CommonStates" };
		common.States.Add(new VisualState { Name = "Normal" });
		common.States.Add(new VisualState
		{
			Name = "Disabled",
			Storyboard = TemplateStates.Hold((this, ForegroundProperty, TemplateStates.DisabledForeground)),
		});
		var selection = new VisualStateGroup { Name = "SelectionStates" };
		selection.States.Add(new VisualState { Name = "Unselected" });
		selection.States.Add(new VisualState { Name = "UnselectedPointerOver", Storyboard = TemplateStates.Background(this, over ?? OverBackground) });
		selection.States.Add(new VisualState { Name = "Selected", Storyboard = Highlight(selected ?? SelectedBackground, selectedText) });
		selection.States.Add(new VisualState
		{
			Name = "SelectedPointerOver",
			Storyboard = Highlight(selectedOver ?? (selected is null ? SelectedOverBackground : selected), selectedText),
		});
		selection.States.Add(new VisualState
		{
			Name = "SelectedDisabled",
			Storyboard = TemplateStates.Background(this, TemplateStates.DisabledAccent),
		});
		SetBuiltInValue(VisualStateManager.VisualStateGroupsProperty, [common, selection]);
	}

	private Storyboard Highlight(Brush background, Brush? text) => text is null
		? TemplateStates.Background(this, background)
		: TemplateStates.Hold((this, BackgroundProperty, background), (this, ForegroundProperty, text));

	protected override Size MeasureOverride(Size available)
	{
		if (!ShowsContent)
			return Size.Zero;
		var padding = Padding;
		var inner = new Size(MathF.Max(0f, available.Width - padding.Horizontal), MathF.Max(0f, available.Height - padding.Vertical));
		var content = base.MeasureOverride(inner);
		return new Size(content.Width + padding.Horizontal, content.Height + padding.Vertical);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var padding = Padding;
		var inner = new Size(MathF.Max(0f, finalSize.Width - padding.Horizontal), MathF.Max(0f, finalSize.Height - padding.Vertical));
		if (Content is UIElement element && Template is null)
			element.Arrange(new Rect(padding.Left, padding.Top, inner.Width, inner.Height));
		else
			base.ArrangeOverride(finalSize);
		return finalSize;
	}
}
