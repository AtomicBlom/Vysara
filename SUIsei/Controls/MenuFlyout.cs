using System.Numerics;

namespace SUIsei.Controls;

/// <summary>
/// A context menu: <see cref="ShowAt"/> opens a list of <see cref="Items"/> in the root's
/// overlay layer just below the target, with a full-surface light-dismiss layer behind it. Invoking an item (which
/// fires its <see cref="ButtonBase.Click"/> / <see cref="ButtonBase.Command"/>) or pressing outside closes it.
/// </summary>
public sealed class MenuFlyout
{
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

		var position = AbsolutePosition(target);
		var panel = new StackPanel { Orientation = Orientation.Vertical };
		foreach (var item in Items)
		{
			item.Click += OnItemClick;
			panel.Children.Add(item);
		}
		_menu = new Border
		{
			Background = HdrColor.FromRgb(45, 45, 55),
			BorderBrush = HdrColor.FromRgb(80, 80, 95),
			BorderThickness = new Thickness(1f),
			CornerRadius = 4f,
			Padding = new Thickness(4f),
			Child = panel,
		};

		_dismissLayer = new Border { Background = HdrColor.Transparent };
		_dismissLayer.AddHandler(UIElement.PointerPressedEvent, OnDismissPressed);

		root.AddOverlay(_dismissLayer, Vector2.Zero, fullSurface: true);
		root.AddOverlay(_menu, new Vector2(position.X, position.Y + target.LayoutRect.Height));
		_root = root;
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

	// The element's position in surface DIPs, summing layout offsets up the parent chain (affine transforms are
	// ignored — sufficient for the translate/scale layout flyouts anchor against).
	private static Vector2 AbsolutePosition(UIElement element)
	{
		var position = Vector2.Zero;
		for (UIElement? e = element; e is not null; e = e.Parent)
			position += new Vector2(e.LayoutRect.X, e.LayoutRect.Y);
		return position;
	}
}
