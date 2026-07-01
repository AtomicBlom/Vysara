using SUIsei.Animation;

namespace SUIsei.Controls;

/// <summary>
/// A top-level entry in a <see cref="MenuBar"/>: a <see cref="ButtonBase"/> showing a
/// <see cref="Title"/> that, when invoked, opens its <see cref="Items"/> as a <see cref="MenuFlyout"/> anchored
/// below it. The flat template highlights on hover/press through a <c>CommonStates</c> group; the owning
/// <see cref="MenuBar"/> keeps at most one item's menu open at a time.
/// </summary>
[ContentProperty(Name = nameof(Items))]
public sealed class MenuBarItem : ButtonBase
{
	private static readonly HdrColor NormalBackground = HdrColor.Transparent;
	private static readonly HdrColor OverBackground = HdrColor.FromRgb(58, 58, 72);
	private static readonly HdrColor PressedBackground = HdrColor.FromRgb(72, 72, 92);

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
				Background = NormalBackground,
				CornerRadius = 3f,
				Padding = new Thickness(14f, 6f),
			};
			var presenter = new ContentPresenter
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			border.Child = presenter;
			presenter.Content = host.Content;
			host.ContentChanged += (_, _) => presenter.Content = host.Content;

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = BackgroundTo(border, OverBackground, NormalBackground) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = BackgroundTo(border, NormalBackground, OverBackground) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = BackgroundTo(border, OverBackground, PressedBackground) });
			VisualStateManager.SetVisualStateGroups(border, [common]);
			return border;
		},
	};

	private static Storyboard BackgroundTo(Border target, HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(90), FillBehavior = FillBehavior.HoldEnd,
				Apply = target.SetAnimatedFill, Release = target.ClearAnimatedFill,
			},
		},
	};
}
