using Vysara.Extensions;

namespace Vysara.Controls;

/// <summary>
/// A vertical items panel that realizes only the containers intersecting its viewport. It assumes one uniform row
/// height (<see cref="Virtualization.RowHeightProperty"/>, <c>vysara:Virtualization.RowHeight</c> in XAML), so the
/// visible window is pure arithmetic from <see cref="VerticalOffset"/>; containers that scroll out are recycled back to
/// the <see cref="IItemContainerSource"/>. It reports the full extent (<c>ItemCount × row height</c>) so an outer
/// scroller sees the true size. It has no variable-height virtualization, scroll chrome, or wheel and scrollbar input:
/// the offset is set directly.
/// </summary>
public sealed class VirtualizingStackPanel : Panel, IVirtualizingItemsHost
{
	private IItemContainerSource? _source;
	private readonly Dictionary<int, UIElement> _realized = [];

	// VisualChildren: the realized containers in item order, rebuilt in place at each measure.
	private readonly List<UIElement> _ordered = [];

	// The realized indices a measure found outside the window, kept so that recycling them allocates nothing.
	private readonly List<int> _outside = [];

	/// <summary>The vertical scroll position, in pixels.</summary>
	public float VerticalOffset { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }

	/// <summary>The full content height (<c>ItemCount × row height</c>), computed at measure — what an outer scroller
	/// reads for its scroll range. <see cref="UIElement.DesiredSize"/> itself is capped to the viewport by layout.</summary>
	public float ExtentHeight { get; private set; }

	void IVirtualizingItemsHost.SetSource(IItemContainerSource source) => _source = source;

	void IVirtualizingItemsHost.Reset()
	{
		foreach (var container in _realized.Values)
		{
			DropChild(container);
			_source?.RecycleContainer(container);
		}
		_realized.Clear();
		_ordered.Clear();
		InvalidateMeasure();
	}

	internal override IReadOnlyList<UIElement> VisualChildren => _ordered;

	protected override Size MeasureOverride(Size available)
	{
		if (_source is null)
			return Size.Zero;

		var count = _source.ItemCount;
		var rowHeight = MathF.Max(1f, Virtualization.GetRowHeight(this));

		// The window of item indices intersecting the viewport (+1 for the partially-scrolled row at the bottom).
		var first = count == 0 ? 0 : Math.Clamp((int)MathF.Floor(VerticalOffset / rowHeight), 0, count - 1);
		var visible = (int)MathF.Ceiling(available.Height / rowHeight) + 1;
		var last = Math.Min(count - 1, first + visible - 1);

		// Recycle containers now outside the window.
		_outside.Clear();
		foreach (var index in _realized.Keys)
			if (index < first || index > last)
				_outside.Add(index);
		foreach (var index in _outside)
		{
			var container = _realized[index];
			_realized.Remove(index);
			DropChild(container);
			_source.RecycleContainer(container);
		}

		// Realize containers now inside the window.
		for (var index = first; index <= last && count > 0; index++)
		{
			if (_realized.ContainsKey(index))
				continue;
			var container = _source.RealizeContainerAt(index);
			_realized[index] = container;
			AdoptChild(container);
		}

		RebuildOrdered(first, last);
		foreach (var container in _ordered)
			container.Measure(new Size(available.Width, rowHeight));

		// Report the full extent so an outer scroller knows the true content height.
		ExtentHeight = count * rowHeight;
		return new Size(available.Width, ExtentHeight);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var rowHeight = MathF.Max(1f, Virtualization.GetRowHeight(this));
		foreach (var (index, container) in _realized)
			container.Arrange(new Rect(0f, index * rowHeight - VerticalOffset, finalSize.Width, rowHeight));
		return finalSize;
	}

	// The realized containers in item order. Measure has just recycled every container outside the window and realized
	// every one inside it, so the realized indices are exactly first to last.
	private void RebuildOrdered(int first, int last)
	{
		_ordered.Clear();
		for (var index = first; index <= last; index++)
			_ordered.Add(_realized[index]);
	}
}
