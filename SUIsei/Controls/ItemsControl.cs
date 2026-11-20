using System.Collections;
using System.Collections.Specialized;

namespace SUIsei.Controls;

/// <summary>
/// Lays out one container per item of <see cref="ItemsSource"/> in an <see cref="ItemsPanel"/>.
/// Each container is realized through <see cref="ItemTemplate"/> (a <see cref="DataTemplate"/>) or, with no
/// template, by wrapping the item. When the source implements <see cref="INotifyCollectionChanged"/> the realized
/// containers are updated incrementally (add/remove/replace/move/reset) rather than rebuilt; the control listens only while
/// it is in a live tree, so a long-lived collection never keeps a removed one alive, and joining a tree again lays the
/// items out anew if the source changed while it was out.
/// </summary>
public class ItemsControl : Control, IItemContainerSource
{
	private Panel? _panel;
	// What the control hosts: the panel, or the scroll viewer holding it.
	private FrameworkElement? _host;
	// VisualChildren, set once the panel is built, so reading it allocates nothing.
	private UIElement[] _panelList = [];
	private bool _virtualizing;
	private readonly List<object?> _items = [];
	private readonly Stack<UIElement> _recyclePool = new();
	private readonly Primitives.SourceSubscription _source;

	public ItemsControl() => _source = new Primitives.SourceSubscription(OnCollectionChanged, ReloadIfChanged);

	/// <summary>Whether the panel is hosted in a <see cref="ScrollViewer"/> of the control's own, as a list's is. A
	/// virtualizing panel never is: it scrolls itself.</summary>
	private protected virtual bool ScrollsItems => false;

	/// <summary>The scroll viewer the items are in, where the control has one.</summary>
	internal ScrollViewer? ItemsScrollViewer => _host as ScrollViewer;

	/// <summary>Containers realized so far (instrumentation for the virtualization tests).</summary>
	internal int RealizeCount { get; private set; }

	/// <summary>Containers recycled so far (instrumentation for the virtualization tests).</summary>
	internal int RecycleCount { get; private set; }

	public IEnumerable? ItemsSource
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			field = value;
			_source.Source = value;
			Reload();
		}
	}

	/// <summary>The template realized for each item; null wraps the item with the default content policy.</summary>
	public DataTemplate? ItemTemplate { get; set { field = value; Reload(); } }

	/// <summary>
	/// The panel the item containers are laid out in. Defaults to a vertical <see cref="StackPanel"/>. Set after the items
	/// are laid out (an <see cref="ItemsSource"/> set first, as markup sets an attribute before a property element), the
	/// old panel is let go and the items are laid out again in a new one.
	/// </summary>
	public ItemsPanelTemplate ItemsPanel
	{
		get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			if (ReferenceEquals(field, value))
				return;
			field = value;
			if (_panel is null)
				return;
			DropChild(_host!);
			_panel = null;
			_host = null;
			_panelList = [];
			_virtualizing = false;
			_recyclePool.Clear();
			Reload();
		}
	} = ItemsPanelTemplate.DefaultStackPanel;

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
			_virtualizing = _panel is IVirtualizingItemsHost;
			_host = ScrollsItems && !_virtualizing ? new ScrollViewer { Content = _panel } : _panel;
			_panelList = [_host];
			AdoptChild(_host);
			if (_panel is IVirtualizingItemsHost host)
				host.SetSource(this);
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

	internal override IReadOnlyList<UIElement> VisualChildren => _panelList;

	protected override Size MeasureOverride(Size available)
	{
		EnsurePanel();
		_host!.Measure(available);
		return _host.DesiredSize;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		_host?.Arrange(new Rect(0f, 0f, finalSize.Width, finalSize.Height));
		return finalSize;
	}

	private protected override void OnLiveTreeChanged(bool attached)
	{
		base.OnLiveTreeChanged(attached);
		_source.LiveTreeChanged(attached);
	}

	// Joined a tree: the items are laid out again only if the source no longer holds what they were laid out from
	private void ReloadIfChanged()
	{
		if (ItemsSource is not { } source || _panel is null)
			return;
		var i = 0;
		foreach (var item in source)
		{
			if (i >= _items.Count || !Equals(_items[i], item))
			{
				Reload();
				return;
			}
			i++;
		}
		if (i != _items.Count)
			Reload();
	}
}
