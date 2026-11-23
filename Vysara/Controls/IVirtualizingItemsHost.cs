namespace Vysara.Controls;

/// <summary>
/// A panel that virtualizes its children by pulling them from an <see cref="IItemContainerSource"/>.
/// An <see cref="ItemsControl"/> detects this on its panel and hands it the source instead of pushing all
/// the containers itself.
/// </summary>
internal interface IVirtualizingItemsHost
{
	/// <summary>Supply the source the panel realizes containers from.</summary>
	void SetSource(IItemContainerSource source);

	/// <summary>Drop all realized containers (the item set changed); the next measure re-pulls the viewport.</summary>
	void Reset();
}
