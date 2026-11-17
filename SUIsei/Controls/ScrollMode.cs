namespace SUIsei.Controls;

/// <summary>Whether a <see cref="ScrollViewer"/> scrolls along an axis, as WinUI's <c>ScrollMode</c>.</summary>
public enum ScrollMode
{
	/// <summary>It doesn't: the content is measured to the viewport along that axis.</summary>
	Disabled,

	/// <summary>It does: the content is measured unbounded along that axis and scrolled when it is larger.</summary>
	Enabled,
}
