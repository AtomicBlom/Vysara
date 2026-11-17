namespace SUIsei.Controls;

/// <summary>
/// Whether a <see cref="ScrollViewer"/> scrolls along an axis and shows its thumb, as WinUI's <c>ScrollBarVisibility</c>.
/// SUIsei's scroll bar is a thumb alone, with no track, so <see cref="Visible"/> shows as <see cref="Auto"/> does: there is no
/// thumb when there is nothing to scroll.
/// </summary>
public enum ScrollBarVisibility
{
	/// <summary>It doesn't scroll that way: the content is measured to the viewport along that axis.</summary>
	Disabled,

	/// <summary>It scrolls that way, the content measured unbounded along it, with a thumb when there is something to scroll.</summary>
	Auto,

	/// <summary>It scrolls that way with no thumb: by the wheel, the keys, a finger or from code.</summary>
	Hidden,

	/// <summary>It scrolls that way, with a thumb when there is something to scroll.</summary>
	Visible,
}
