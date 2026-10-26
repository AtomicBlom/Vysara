using System.Numerics;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A context menu: <see cref="ShowAt"/> opens a list of <see cref="Items"/> in the root's
/// overlay layer just below the target, with a full-surface light-dismiss layer behind it. Invoking an item (which
/// fires its <see cref="ButtonBase.Click"/> / <see cref="ButtonBase.Command"/>) or pressing outside closes it.
/// </summary>
public sealed class MenuFlyout
{
	// Leaves this much room off the bottom edge so a full-height flyout doesn't touch the surface boundary, and
	// never shrinks a flyout below this even when the anchor sits close to the bottom — a sliver-height scroll
	// area is unusable, so it's better to let a few items peek past the edge than collapse to nothing.
	private const float BottomMargin = 8f;
	private const float MinMenuHeight = 48f;

	private UIRoot? _root;
	private Border? _menu;
	private Border? _dismissLayer;

	public List<MenuFlyoutItem> Items { get; } = [];

	public bool IsOpen => _root is not null;

	/// <summary>Raised when the menu closes (item invoke, light dismiss, or an explicit <see cref="Hide"/>) — lets an
	/// owner such as <see cref="MenuBar"/> drop its open-item bookkeeping.</summary>
	public event Action? Closed;

	/// <summary>Open the menu anchored under <paramref name="target"/> (which must be attached to a live root).</summary>
	public void ShowAt(UIElement target)
	{
		if (target.AttachedRoot is not { } root)
			return;
		Hide();

		// Under the target as drawn: its bounds in root DIPs, through its composed transforms.
		var anchor = target.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, target.LayoutRect.Width, target.LayoutRect.Height));
		var position = new Vector2(anchor.X, anchor.Y);
		var menuTop = anchor.Bottom;
		var panel = new StackPanel { Orientation = Orientation.Vertical };
		foreach (var item in Items)
		{
			item.Click += OnItemClick;
			panel.Children.Add(item);
		}
		var scroll = new ScrollViewer { Content = panel, MaxHeight = MaxMenuHeight(root, menuTop) };
		_menu = new Border
		{
			Background = HdrColor.FromRgb(45, 45, 55),
			BorderBrush = HdrColor.FromRgb(80, 80, 95),
			BorderThickness = new Thickness(1f),
			CornerRadius = 4f,
			Padding = new Thickness(4f),
			Child = scroll,
		};

		_dismissLayer = new Border { Background = HdrColor.Transparent };
		_dismissLayer.AddHandler(UIElement.PointerPressedEvent, OnDismissPressed);

		root.AddOverlay(_dismissLayer, Vector2.Zero, fullSurface: true);
		root.AddOverlay(_menu, new Vector2(position.X, menuTop));
		_root = root;
	}

	// The vertical room below the anchor, in DIPs — a menu taller than this scrolls (via the ScrollViewer wrapping
	// its items) rather than spilling past the surface edge. Unbounded until the first layout pass has run (no
	// known surface size yet), matching the previous unclamped behaviour for that brief window.
	private static float MaxMenuHeight(UIRoot root, float menuTop)
	{
		var surfaceHeight = root.Visual.SurfaceSize.Y;
		if (surfaceHeight <= 0f)
			return float.PositiveInfinity;
		var scale = root.LayoutScale > 0f ? root.LayoutScale : 1f;
		return MathF.Max(surfaceHeight / scale - menuTop - BottomMargin, MinMenuHeight);
	}

	public void Hide()
	{
		if (_root is null)
			return;
		foreach (var item in Items)
			item.Click -= OnItemClick;
		if (_menu is not null)
			_root.RemoveOverlay(_menu);
		if (_dismissLayer is not null)
			_root.RemoveOverlay(_dismissLayer);
		_root = null;
		_menu = null;
		_dismissLayer = null;
		Closed?.Invoke();
	}

	private void OnItemClick(object sender, RoutedEventArgs e) => Hide();

	private void OnDismissPressed(object sender, RoutedEventArgs e)
	{
		Hide();
		e.Handled = true;
	}
}
