namespace BinaryVibrance.Vysara;

/// <summary>Controls how Tab navigation behaves within a <see cref="Controls.Control"/> subtree (WinUI-compatible).</summary>
public enum TabFocusNavigation
{
	/// <summary>Tab moves freely into and out of this group (default).</summary>
	Local,
	/// <summary>Tab wraps within this group instead of exiting to the next sibling scope.</summary>
	Cycle,
	/// <summary>The entire group is treated as a single tab stop; Tab skips directly to the next group.</summary>
	Once,
}
