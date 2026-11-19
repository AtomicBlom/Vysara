using System.Numerics;

namespace SUIsei.Controls.Primitives;

/// <summary>
/// Where something opened against an anchor goes (a tooltip, a flyout): on the side asked for, a gap clear of the anchor,
/// turned to the opposite side when it would leave the bounds there and fits better on that side, and then slid along to
/// stay inside the bounds. All in the root's DIPs.
/// </summary>
internal static class AnchoredPlacement
{
	/// <summary>The top-left of something of <paramref name="size"/> placed against <paramref name="anchor"/> within
	/// <paramref name="bounds"/>; <paramref name="pointer"/> is the anchor for <see cref="PlacementMode.Mouse"/>.</summary>
	public static Vector2 Place(Rect anchor, Size size, Size bounds, PlacementMode mode, float gap, Vector2 pointer = default)
	{
		if (mode == PlacementMode.Mouse)
		{
			// Below and right of the pointer, as a cursor's tip is; a point anchor has no sides to turn round
			anchor = new Rect(pointer.X, pointer.Y, 0f, 16f);
			mode = PlacementMode.Bottom;
		}

		float x, y;
		if (mode is PlacementMode.Bottom or PlacementMode.Top)
		{
			var below = anchor.Bottom + gap;
			var above = anchor.Y - gap - size.Height;
			var roomBelow = bounds.Height - below;
			var roomAbove = anchor.Y - gap;
			y = mode == PlacementMode.Bottom
				? size.Height <= roomBelow || roomBelow >= roomAbove ? below : above
				: size.Height <= roomAbove || roomAbove >= roomBelow ? above : below;
			x = anchor.X + (anchor.Width - size.Width) / 2f;
		}
		else
		{
			var right = anchor.Right + gap;
			var left = anchor.X - gap - size.Width;
			var roomRight = bounds.Width - right;
			var roomLeft = anchor.X - gap;
			x = mode == PlacementMode.Right
				? size.Width <= roomRight || roomRight >= roomLeft ? right : left
				: size.Width <= roomLeft || roomLeft >= roomRight ? left : right;
			y = anchor.Y + (anchor.Height - size.Height) / 2f;
		}

		x = MathF.Max(0f, MathF.Min(x, bounds.Width - size.Width));
		y = MathF.Max(0f, MathF.Min(y, bounds.Height - size.Height));
		return new Vector2(x, y);
	}
}
