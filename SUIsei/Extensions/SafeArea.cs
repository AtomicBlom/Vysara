namespace SUIsei.Extensions;

/// <summary>
/// Keeps content clear of the parts of the screen that hide it: a phone's camera cutout, its rounded corners. Set
/// <c>suisei:SafeArea.Edges="All"</c> (or <c>Top</c>, <c>Left</c>, …) on the element whose content must stay visible,
/// typically the page's root: it is arranged inset by however much of it lies in the unsafe band along each edge it
/// names, so a background can still run to the very edge of the screen while its content stays clear.
/// </summary>
/// <remarks>
/// The bands come from the root: the host sets <see cref="UIRoot.SafeAreaInsets"/> (in surface pixels) to how far in
/// from each edge the screen hides content, and leaves it zero on a display that hides none. The element's position is
/// taken from its arranged ancestors, so a render transform or a scroll offset above it is not accounted for.
/// </remarks>
public sealed class SafeArea : DependencyObject
{
	private SafeArea()
	{
	}

	/// <summary>The edges along which the element keeps clear of the unsafe band. None by default.</summary>
	public static readonly DependencyProperty<SafeArea, SafeAreaEdges> EdgesProperty =
		DependencyProperty.RegisterAttached<SafeArea, SafeAreaEdges>("Edges",
			new PropertyMetadata<SafeArea, SafeAreaEdges> { DefaultValue = SafeAreaEdges.None, Invalidates = InvalidationFlags.Arrange });

	public static void SetEdges(UIElement element, SafeAreaEdges value) => element.SetValue(EdgesProperty, value);
	public static SafeAreaEdges GetEdges(UIElement element) => element.GetValue(EdgesProperty);

	/// <summary>
	/// The slot <paramref name="element"/> is arranged into, inset along each edge it names by the part of it in that
	/// edge's unsafe band: <paramref name="slot"/> itself when it names none, is not in a live tree, or is clear of them.
	/// </summary>
	internal static Rect Inset(UIElement element, Rect slot)
	{
		if (element.AttachedRoot is not { SafeRect: { } safe })
			return slot;
		var edges = GetEdges(element);
		if (edges == SafeAreaEdges.None)
			return slot;

		// The slot is in the parent's space; each arranged ancestor's placement takes it on to the root's.
		var left = slot.X;
		var top = slot.Y;
		for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
		{
			left += ancestor.LayoutRect.X;
			top += ancestor.LayoutRect.Y;
		}

		// The element's own box is the slot less its margin: a margin already clear of a band needs no inset.
		var m = element is FrameworkElement framework ? framework.Margin : default;
		var insetLeft = (edges & SafeAreaEdges.Left) != 0 ? MathF.Max(0f, safe.X - (left + m.Left)) : 0f;
		var insetTop = (edges & SafeAreaEdges.Top) != 0 ? MathF.Max(0f, safe.Y - (top + m.Top)) : 0f;
		var insetRight = (edges & SafeAreaEdges.Right) != 0 ? MathF.Max(0f, left + slot.Width - m.Right - (safe.X + safe.Width)) : 0f;
		var insetBottom = (edges & SafeAreaEdges.Bottom) != 0 ? MathF.Max(0f, top + slot.Height - m.Bottom - (safe.Y + safe.Height)) : 0f;
		return new Rect(slot.X + insetLeft, slot.Y + insetTop,
			MathF.Max(0f, slot.Width - insetLeft - insetRight), MathF.Max(0f, slot.Height - insetTop - insetBottom));
	}
}
