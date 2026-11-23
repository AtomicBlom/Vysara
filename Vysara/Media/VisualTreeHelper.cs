namespace Vysara.Media;

/// <summary>
/// The visual tree walked from outside it, as WinUI's <c>VisualTreeHelper</c>: an element's parent, and its children in
/// the order they are drawn. A template's parts are children of the control they make up.
/// </summary>
public static class VisualTreeHelper
{
	/// <summary>The element <paramref name="reference"/> is a child of, or null at the top of its tree or out of one.</summary>
	public static UIElement? GetParent(UIElement reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return reference.Parent;
	}

	/// <summary>How many children <paramref name="reference"/> has in the visual tree.</summary>
	public static int GetChildrenCount(UIElement reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return reference.VisualChildren.Count;
	}

	/// <summary>The child of <paramref name="reference"/> at <paramref name="childIndex"/>, in the order they are drawn.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="childIndex"/> isn't one of its children.</exception>
	public static UIElement GetChild(UIElement reference, int childIndex)
	{
		ArgumentNullException.ThrowIfNull(reference);
		var children = reference.VisualChildren;
		ArgumentOutOfRangeException.ThrowIfNegative(childIndex);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(childIndex, children.Count);
		return children[childIndex];
	}
}
