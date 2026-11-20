using SUIsei.Controls.Primitives;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// The container a <see cref="ListView"/> wraps each item in, as WinUI's. Its built-in template is one
/// <see cref="ListViewItemPresenter"/>, as WinUI's default style is: the content laid out across the row inside the item's
/// <see cref="Control.Padding"/>, over the item's <see cref="Control.Background"/> (clear unless set, but drawn, so the
/// whole row takes the pointer), with a highlight as the pointer passes over or presses it and while it is selected. A page
/// recolours the highlights with WinUI's <c>ListViewItemBackgroundPointerOver</c>, <c>ListViewItemBackgroundPressed</c>,
/// <c>ListViewItemBackgroundSelected</c>, <c>ListViewItemBackgroundSelectedPointerOver</c>,
/// <c>ListViewItemBackgroundSelectedPressed</c> and <c>ListViewItemForegroundSelected</c> resources, read as the template is
/// built. Disabled, its text takes the disabled text brush, and a selected item shows a highlight without the accent.
/// <see cref="IsSelected"/> is settable, as WinUI's, and selects the item in its list. An item whose content is collapsed
/// takes no room, so a list filtered by hiding its items' content closes up. A template set on the item replaces the
/// presenter, and the item's states with it.
/// </summary>
public sealed class ListViewItem : ContentControl
{
	private static readonly SolidColorBrush SelectedBackground = Brush.Sealed(HdrColor.FromRgb(50, 90, 160));
	private static readonly SolidColorBrush OverBackground = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 18));
	private static readonly SolidColorBrush PressedBackground = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 10));
	private static readonly SolidColorBrush SelectedOverBackground = Brush.Sealed(HdrColor.FromRgb(62, 104, 178));
	// Clear, but drawn, so the whole row takes the pointer and not only its text
	private static readonly SolidColorBrush RowBackground = Brush.Sealed(HdrColor.Transparent);
	private static readonly Thickness BuiltInPadding = new(10f, 5f, 10f, 5f);

	public static readonly DependencyProperty<ListViewItem, bool> IsSelectedProperty =
		DependencyProperty.Register(nameof(IsSelected),
			new PropertyMetadata<ListViewItem, bool> { PropertyChanged = static (i, _, selected) => i.OnIsSelectedChanged(selected) });

	private bool _isPointerOver;
	private bool _isPressed;
	// True while the owning list sets IsSelected, so the change isn't handed back to it
	private bool _syncing;
	private ListViewItemPresenter? _presenter;

	public ListViewItem()
	{
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		SetBuiltInValue(PaddingProperty, BuiltInPadding);
		SetBuiltInValue(BackgroundProperty, RowBackground);
		AddHandler(PointerEnteredEvent, (_, _) => Hover(over: true, pressed: _isPressed));
		AddHandler(PointerExitedEvent, (_, _) => Hover(over: false, pressed: false));
		AddHandler(PointerPressedEvent, (_, e) =>
		{
			if (e is not PointerRoutedEventArgs { IsSecondaryButton: true })
				Hover(_isPointerOver, pressed: true);
		});
		AddHandler(PointerReleasedEvent, (_, _) => Hover(_isPointerOver, pressed: false));
	}

	/// <summary>The data item this container represents (used by the list to map selection back to the source).</summary>
	internal object? DataItem { get; init; }

	/// <summary>The list this item is a container of, if any.</summary>
	internal ListView? Owner { get; init; }

	/// <summary>Whether this item is selected. Setting it selects (or clears) the item in its list.</summary>
	public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

	/// <summary>Whether the item shows anything: its content is not collapsed.</summary>
	internal bool ShowsContent => Content is not UIElement { Visibility: Visibility.Collapsed };

	/// <summary>The list selected or let go of this item.</summary>
	internal void SetSelected(bool selected)
	{
		_syncing = true;
		try
		{
			IsSelected = selected;
		}
		finally
		{
			_syncing = false;
		}
	}

	private void OnIsSelectedChanged(bool selected)
	{
		ShowState(useTransitions: true);
		if (!_syncing)
			Owner?.OnItemIsSelectedChanged(this, selected);
	}

	private void Hover(bool over, bool pressed)
	{
		if (over == _isPointerOver && pressed == _isPressed)
			return;
		_isPointerOver = over;
		_isPressed = pressed;
		ShowState(useTransitions: true);
	}

	// A disabled item sees no input, so a hover or press in progress could never end: both are dropped
	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		_isPointerOver = false;
		_isPressed = false;
		ShowState(useTransitions: true);
	}

	private void ShowState(bool useTransitions) =>
		_presenter?.Show(new ListViewItemState(IsSelected, _isPointerOver, _isPressed, IsEnabled), useTransitions);

	protected override Size MeasureOverride(Size available) => ShowsContent ? base.MeasureOverride(available) : Size.Zero;

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var item = (ListViewItem)control;
			var selected = item.ThemeResourceOrNull("ListViewItemBackgroundSelected");
			var over = item.ThemeResource("ListViewItemBackgroundPointerOver", OverBackground);
			var presenter = new ListViewItemPresenter
			{
				BackgroundTransition = TemplateStates.Fade(80),
				PointerOverBackground = over,
				PressedBackground = item.ThemeResource("ListViewItemBackgroundPressed", PressedBackground),
				SelectedBackground = selected ?? SelectedBackground,
				// A page that recolours the selection alone has it hovered in the same colour, not the built-in one
				SelectedPointerOverBackground = item.ThemeResource("ListViewItemBackgroundSelectedPointerOver", selected ?? SelectedOverBackground),
				SelectedPressedBackground = item.ThemeResourceOrNull("ListViewItemBackgroundSelectedPressed"),
				SelectedDisabledBackground = TemplateStates.DisabledAccent,
				SelectedForeground = item.ThemeResourceOrNull("ListViewItemForegroundSelected"),
			};
			item.RegisterTemplateBinding(BackgroundProperty, presenter, ContentPresenter.BackgroundProperty);
			item.RegisterTemplateBinding(ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
			item.RegisterTemplateBinding(PaddingProperty, presenter, ListViewItemPresenter.ContentMarginProperty);
			item.RegisterContentTemplateBinding(content => presenter.Content = content);
			item._presenter = presenter;
			item.ShowState(useTransitions: false);
			return presenter;
		},
	};

	// A template of the app's own replaces the presenter, which then shows nothing more
	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (_presenter is not null && !ReferenceEquals(Template, DefaultTemplate))
			_presenter = null;
	}
}
