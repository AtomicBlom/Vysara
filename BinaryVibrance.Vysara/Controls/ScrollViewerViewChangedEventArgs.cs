namespace BinaryVibrance.Vysara.Controls;

/// <summary>A <see cref="ScrollViewer"/>'s view moved, as WinUI's <c>ScrollViewerViewChangedEventArgs</c>. Vysara's
/// scrolling has no inertia, so a change is never intermediate.</summary>
public sealed class ScrollViewerViewChangedEventArgs : EventArgs
{
	internal static readonly ScrollViewerViewChangedEventArgs Final = new();

	/// <summary>Whether more of the same change is coming: always false.</summary>
	public bool IsIntermediate => false;
}
