namespace Vysara.Controls;

/// <summary>
/// The contract a virtualizing panel pulls item containers from — implemented by
/// <see cref="ItemsControl"/>. The panel realizes only the containers in its viewport and recycles the rest, so a
/// large list never materializes all its containers at once.
/// </summary>
public interface IItemContainerSource
{
	/// <summary>The number of items.</summary>
	int ItemCount { get; }

	/// <summary>Realize (or recycle-and-rebind) the container for the item at <paramref name="index"/>.</summary>
	UIElement RealizeContainerAt(int index);

	/// <summary>Return a container that scrolled out of view to the recycle pool.</summary>
	void RecycleContainer(UIElement container);
}
