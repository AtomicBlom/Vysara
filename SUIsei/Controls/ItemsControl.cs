using System.Collections;
using System.Collections.Specialized;

namespace SUIsei.Controls;

/// <summary>
/// Lays out one container per item of <see cref="ItemsSource"/> in an <see cref="ItemsPanel"/>.
/// Each container is realized through <see cref="ItemTemplate"/> (a <see cref="DataTemplate"/>) or, with no
/// template, by wrapping the item. When the source implements <see cref="INotifyCollectionChanged"/> the realized
/// containers are updated incrementally (add/remove/replace/move/reset) rather than rebuilt.
/// </summary>
public class ItemsControl : Control, IItemContainerSource
{
	private Panel? _panel;
	private bool _virtualizing;
	private readonly List<object?> _items = [];
	private readonly Stack<UIElement> _recyclePool = new();
	private INotifyCollectionChanged? _incc;

	/// <summary>Containers realized so far (instrumentation for the virtualization tests).</summary>
	public int RealizeCount { get; private set; }

	/// <summary>Containers recycled so far (instrumentation for the virtualization tests).</summary>
	public int RecycleCount { get; private set; }

	public IEnumerable? ItemsSource
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			if (_incc is not null)
				_incc.CollectionChanged -= OnCollectionChanged;
			field = value;
			_incc = value as INotifyCollectionChanged;
			if (_incc is not null)
				_incc.CollectionChanged += OnCollectionChanged;
			Reload();
		}
	}

	/// <summary>The template realized for each item; null wraps the item with the default content policy.</summary>
	public DataTemplate? ItemTemplate { get; set { field = value; Reload(); } }

	/// <summary>The panel the item containers are laid out in. Defaults to a vertical <see cref="StackPanel"/>.</summary>
	public ItemsPanelTemplate ItemsPanel { get; set; } = ItemsPanelTemplate.DefaultStackPanel;

	/// <summary>The materialized item list (a snapshot of <see cref="ItemsSource"/>).</summary>
	protected internal IReadOnlyList<object?> Items => _items;

	/// <summary>The panel hosting the realized item containers, in order.</summary>
	internal Panel? ItemsHost => _panel;

	/// <summary>Realize a container for <paramref name="item"/> — the data template, else the wrapped item.</summary>
	protected virtual UIElement RealizeContainer(object? item) =>
		ItemTemplate is { } dt ? dt.Factory(item) : ContentHelper.Wrap(item) ?? new TextBlock();

	private Panel EnsurePanel()
	{
		if (_panel is null)
		{
			_panel = ItemsPanel.Build();
			AdoptChild(_panel);
			if (_panel is IVirtualizingItemsHost host)
			{
				_virtualizing = true;
				host.SetSource(this);
			}
		}
		return _panel;
	}

	private void Reload()
	{
		var panel = EnsurePanel();
		_items.Clear();
		if (ItemsSource is not null)
			foreach (var item in ItemsSource)
				_items.Add(item);

		// A virtualizing panel pulls only the visible containers itself; a plain panel hosts them all.
		if (_virtualizing)
			((IVirtualizingItemsHost)panel).Reset();
		else
		{
			panel.Children.Clear();
			foreach (var item in _items)
				panel.Children.Add(RealizeContainer(item));
		}

		InvalidateMeasure();
		OnReload();
	}

	/// <summary>Called after the full item set is (re)materialized — a derived list resets per-container state (e.g.
	/// selection) here.</summary>
	protected virtual void OnReload() { }

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		var panel = EnsurePanel();

		// _items stays authoritative in both modes; only a plain panel mirrors the change into its children
		// (a virtualizing panel re-pulls the visible window from the updated item list after a Reset, below).
		switch (e.Action)
		{
			case NotifyCollectionChangedAction.Add when e.NewItems is not null:
				for (var i = 0; i < e.NewItems.Count; i++)
				{
					var index = e.NewStartingIndex + i;
					_items.Insert(index, e.NewItems[i]);
					if (!_virtualizing)
						panel.Children.Insert(index, RealizeContainer(e.NewItems[i]));
				}
				break;

			case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
				for (var i = e.OldItems.Count - 1; i >= 0; i--)
				{
					var index = e.OldStartingIndex + i;
					_items.RemoveAt(index);
					if (!_virtualizing)
						panel.Children.RemoveAt(index);
				}
				break;

			case NotifyCollectionChangedAction.Replace when e.NewItems is not null:
				_items[e.NewStartingIndex] = e.NewItems[0];
				if (!_virtualizing)
				{
					panel.Children.RemoveAt(e.NewStartingIndex);
					panel.Children.Insert(e.NewStartingIndex, RealizeContainer(e.NewItems[0]));
				}
				break;

			case NotifyCollectionChangedAction.Move:
				var movedItem = _items[e.OldStartingIndex];
				_items.RemoveAt(e.OldStartingIndex);
				_items.Insert(e.NewStartingIndex, movedItem);
				if (!_virtualizing)
				{
					var movedContainer = panel.Children[e.OldStartingIndex];
					panel.Children.RemoveAt(e.OldStartingIndex);
					panel.Children.Insert(e.NewStartingIndex, movedContainer);
				}
				break;

			default: // Reset
				Reload();
				return;
		}

		if (_virtualizing)
			((IVirtualizingItemsHost)panel).Reset();
	}

	// --- IItemContainerSource (the pull contract a virtualizing panel uses) ---

	int IItemContainerSource.ItemCount => _items.Count;

	UIElement IItemContainerSource.RealizeContainerAt(int index)
	{
		RealizeCount++;
		var item = _items[index];
		// Reuse a pooled container when it can be rebound (the default-wrap case); a templated container's bindings
		// are baked to its original item, so it is rebuilt instead.
		if (ItemTemplate is null && _recyclePool.Count > 0)
		{
			var reused = _recyclePool.Pop();
			RebindContainer(reused, item);
			return reused;
		}
		return RealizeContainer(item);
	}

	void IItemContainerSource.RecycleContainer(UIElement container)
	{
		RecycleCount++;
		if (ItemTemplate is null)
			_recyclePool.Push(container);
	}

	/// <summary>Rebind a recycled container to a new item (the default updates a wrapped <see cref="TextBlock"/>).</summary>
	protected virtual void RebindContainer(UIElement container, object? item)
	{
		if (container is TextBlock text)
			text.Text = item?.ToString() ?? string.Empty;
	}

	internal override IReadOnlyList<UIElement> VisualChildren => _panel is null ? [] : [_panel];

	protected override Size MeasureOverride(Size available)
	{
		var panel = EnsurePanel();
		panel.Measure(available);
		return panel.DesiredSize;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		_panel?.Arrange(new Rect(0f, 0f, finalSize.Width, finalSize.Height));
		return finalSize;
	}
}
