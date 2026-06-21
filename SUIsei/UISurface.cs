
using SUIsei.Drawing;

namespace SUIsei;

/// <summary>Well-known <see cref="DrawListId"/>s for the UI host. The overlay HUD is list 0; in-world surfaces take higher ids.</summary>
public static class UISurface
{
	/// <summary>The screen-space overlay surface (the HUD), composited at native resolution over the backbuffer.</summary>
	public static readonly DrawListId Hud = new(0);
}
