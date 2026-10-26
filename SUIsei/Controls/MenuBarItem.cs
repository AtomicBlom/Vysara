using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A top-level entry in a <see cref="MenuBar"/>: a <see cref="ButtonBase"/> showing a
/// <see cref="Title"/> that, when invoked, opens its <see cref="Items"/> as a <see cref="MenuFlyout"/> anchored
/// below it. The flat template highlights on hover/press through a <c>CommonStates</c> group, and, as WinUI's default
/// template, paints the item's <see cref="Control.Background"/>, <see cref="Control.BorderBrush"/> and
/// <see cref="Control.BorderThickness"/>, and its label in <see cref="Control.Foreground"/>; the owning
/// <see cref="MenuBar"/> keeps at most one item's menu open at a time.
/// </summary>
[ContentProperty(Name = nameof(Items))]
public sealed class MenuBarItem : ButtonBase
{
	// The built-in look's brushes, shared by every bar item and never changed.
	private static readonly SolidColorBrush NormalBackground = new(HdrColor.Transparent);
	private static readonly SolidColorBrush OverBackground = new(HdrColor.FromRgb(58, 58, 72));
	private static readonly SolidColorBrush PressedBackground = new(HdrColor.FromRgb(72, 72, 92));

	private readonly MenuFlyout _flyout = new();

	public MenuBarItem()
	{
		Template = DefaultTemplate;
		_flyout.Closed += OnFlyoutClosed;
	}

	/// <summary>The drop-down entries shown when this item is invoked (the menu's <see cref="MenuFlyout.Items"/>).</summary>
	public List<MenuFlyoutItem> Items => _flyout.Items;

	/// <summary>The bar label (maps to <see cref="ContentControl.Content"/>).</summary>
	public string Title
	{
		get => Content as string ?? string.Empty;
		set => Content = value;
	}

	/// <summary>True while this item's drop-down menu is open.</summary>
	public bool IsSubmenuOpen => _flyout.IsOpen;

	// The owning bar, found by walking up the element tree (null when the item is used standalone).
	private MenuBar? OwningBar
	{
		get
		{
			for (UIElement? e = Parent; e is not null; e = e.Parent)
				if (e is MenuBar bar)
					return bar;
			return null;
		}
	}

	/// <summary>Open this item's drop-down (closing any sibling already open). Idempotent.</summary>
	public void OpenMenu()
	{
		if (_flyout.IsOpen)
			return;
		OwningBar?.NotifyOpening(this);
		_flyout.ShowAt(this);
	}

	/// <summary>Close this item's drop-down (a no-op when already closed).</summary>
	public void CloseMenu() => _flyout.Hide();

	// A click toggles the drop-down; the base raises Click / runs any Command first.
	protected override void OnClick()
	{
		base.OnClick();
		if (_flyout.IsOpen)
			CloseMenu();
		else
			OpenMenu();
	}

	private void OnFlyoutClosed() => OwningBar?.NotifyClosed(this);

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var host = (ContentControl)control;
			var border = new Border
			{
				CornerRadius = new CornerRadius(3f),
				Padding = new Thickness(14f, 6f),
				BackgroundTransition = TemplateStates.Fade(90),
			};
			var presenter = new ContentPresenter
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			border.Child = presenter;
			host.RegisterTemplateBinding(BackgroundProperty, value => border.Background = value ?? NormalBackground);
			host.RegisterTemplateBinding(BorderBrushProperty, value => border.BorderBrush = value);
			host.RegisterTemplateBinding(BorderThicknessProperty, value => border.BorderThickness = value);
			host.RegisterTemplateBinding(ForegroundProperty, value => presenter.Foreground = value);
			presenter.Content = host.Content;
			host.ContentChanged += (_, _) => presenter.Content = host.Content;

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(border, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(border, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};
}
