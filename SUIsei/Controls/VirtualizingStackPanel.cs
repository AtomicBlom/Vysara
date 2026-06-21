namespace SUIsei.Controls;

/// <summary>
/// A vertical items panel that realizes only the containers intersecting its viewport (PlanningUI.md §D1.6). MU4
/// assumes a uniform <see cref="RowHeight"/>, so the visible window is pure arithmetic from <see cref="VerticalOffset"/>;
/// containers that scroll out are recycled back to the <see cref="IItemContainerSource"/>. It reports the full extent
/// (<c>ItemCount × RowHeight</c>) so an outer scroller sees the true size. Variable-height virtualization, a scroll
/// chrome, and wheel/scrollbar input are later refinements; the offset is set directly here.
/// </summary>
public sealed class VirtualizingStackPanel : Panel, IVirtualizingItemsHost
{
	private IItemContainerSource? _source;
	private readonly Dictionary<int, UIElement> _realized = [];
	private UIElement[] _ordered = [];

	/// <summary>The uniform per-item height the visible window is computed against.</summary>
	public float RowHeight { get; set { if (field != value) { field = value; InvalidateMeasure(); } } } = 24f;

	/// <summary>The vertical scroll position, in pixels.</summary>
	public float VerticalOffset { get; set { if (field != value) { field = value; InvalidateMeasure(); } } }

	/// <summary>The full content height (<c>ItemCount × RowHeight</c>), computed at measure — what an outer scroller
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
		_ordered = [];
		InvalidateMeasure();
	}

	internal override IReadOnlyList<UIElement> VisualChildren => _ordered;

	protected override Size MeasureOverride(Size available)
	{
		if (_source is null)
			return Size.Zero;

		var count = _source.ItemCount;
		var rowHeight = MathF.Max(1f, RowHeight);

		// The window of item indices intersecting the viewport (+1 for the partially-scrolled row at the bottom).
		var first = count == 0 ? 0 : Math.Clamp((int)MathF.Floor(VerticalOffset / rowHeight), 0, count - 1);
		var visible = (int)MathF.Ceiling(available.Height / rowHeight) + 1;
		var last = Math.Min(count - 1, first + visible - 1);

		// Recycle containers now outside the window.
		List<int>? drop = null;
		foreach (var index in _realized.Keys)
			if (index < first || index > last)
				(drop ??= []).Add(index);
		if (drop is not null)
		{
			foreach (var index in drop)
			{
				var container = _realized[index];
				_realized.Remove(index);
				DropChild(container);
				_source.RecycleContainer(container);
			}
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

		RebuildOrdered();
		foreach (var container in _ordered)
			container.Measure(new Size(available.Width, rowHeight));

		// Report the full extent so an outer scroller knows the true content height.
		ExtentHeight = count * rowHeight;
		return new Size(available.Width, ExtentHeight);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var rowHeight = MathF.Max(1f, RowHeight);
		foreach (var (index, container) in _realized)
			container.Arrange(new Rect(0f, index * rowHeight - VerticalOffset, finalSize.Width, rowHeight));
		return finalSize;
	}

	private void RebuildOrdered()
	{
		var keys = new List<int>(_realized.Keys);
		keys.Sort();
		_ordered = new UIElement[keys.Count];
		for (var i = 0; i < keys.Count; i++)
			_ordered[i] = _realized[keys[i]];
	}
}
