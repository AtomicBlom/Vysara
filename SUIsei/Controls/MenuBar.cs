using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// A horizontal bar of <see cref="MenuBarItem"/>s — the classic application menu (File, Edit, …).
/// Items lay out left to right over a solid bar <see cref="Panel.Background"/>; the bar keeps at most one item's
/// drop-down open at a time, closing the previous one when another opens.
/// </summary>
public sealed class MenuBar : Panel
{
	private MenuBarItem? _openItem;

	public MenuBar() => Background = new SolidColorBrush(HdrColor.FromRgb(34, 37, 46));

	/// <summary>True while one of the bar's items has its drop-down menu open.</summary>
	public bool IsAnyMenuOpen => _openItem is not null;

	// Called by an item just before it opens: close whichever sibling is currently open, then record the new one.
	internal void NotifyOpening(MenuBarItem item)
	{
		if (_openItem is not null && !ReferenceEquals(_openItem, item))
			_openItem.CloseMenu();
		_openItem = item;
	}

	// Called by an item when its menu closes (invoke / light dismiss / explicit close).
	internal void NotifyClosed(MenuBarItem item)
	{
		if (ReferenceEquals(_openItem, item))
			_openItem = null;
	}

	protected override Size MeasureOverride(Size available)
	{
		var width = 0f;
		var height = 0f;
		foreach (var child in Children.Items)
		{
			child.Measure(available with { Width = float.PositiveInfinity });
			width += child.DesiredSize.Width;
			height = MathF.Max(height, child.DesiredSize.Height);
		}
		return new Size(width, height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var x = 0f;
		foreach (var child in Children.Items)
		{
			var w = child.DesiredSize.Width;
			child.Arrange(new Rect(x, 0f, w, finalSize.Height));
			x += w;
		}
		return finalSize;
	}
}
