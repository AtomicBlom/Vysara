namespace Vysara.Controls;

/// <summary>
/// Whether a finger panning a <see cref="ScrollViewer"/> scrolls it along an axis, as WinUI's <c>ScrollMode</c>. It governs
/// only panning: whether the content is laid out to scroll that way at all is the axis's <see cref="ScrollBarVisibility"/>,
/// and the wheel, the keys and a thumb scroll whatever is laid out to scroll.
/// </summary>
public enum ScrollMode
{
	/// <summary>A pan doesn't scroll along that axis.</summary>
	Disabled,

	/// <summary>A pan scrolls along that axis when there is something to scroll.</summary>
	Enabled,

	/// <summary>As <see cref="Enabled"/>.</summary>
	Auto,
}
