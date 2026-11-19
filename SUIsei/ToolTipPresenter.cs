using System.Numerics;
using SUIsei.Controls;
using SUIsei.Controls.Primitives;

namespace SUIsei;

/// <summary>
/// A host's tooltips (<see cref="ToolTipService"/>): ticked once a frame after input, it finds the element with a tooltip
/// nearest the pointer's leaf, counts how long the pointer has rested on it, and shows the tooltip in that element's
/// root's overlay layer, placed against it. One shows at a time. A press, a wheel turn or a key hides it, and it stays
/// hidden until the pointer moves to another element. A steady frame allocates nothing; showing one makes its panel.
/// </summary>
internal sealed class ToolTipPresenter(InputManager input)
{
	// The gap between an element and its tooltip, in DIPs.
	private const float Gap = 6f;

	private UIElement? _owner;
	private double _rested;
	private bool _dismissed;
	private ToolTip? _tip;
	private UIRoot? _root;
	private object? _shownValue;

	/// <summary>The tooltip showing, or null.</summary>
	internal ToolTip? Showing => _tip;

	/// <summary>Hides the tooltip showing, and keeps it hidden until the pointer moves to another element.</summary>
	internal void Dismiss()
	{
		Hide();
		_dismissed = true;
	}

	/// <summary>Once a frame, after input: follows the pointer and shows, places or hides the tooltip.</summary>
	internal void Update(double deltaSeconds)
	{
		var leaf = input.HoverLeaf(out var pointer, out var held);
		// A button held down is no rest: the tooltip goes, and stays gone until the pointer moves to another element
		if (held)
		{
			Dismiss();
			return;
		}
		var owner = Owner(leaf);
		if (!ReferenceEquals(owner, _owner))
		{
			Hide();
			_owner = owner;
			_rested = 0;
			_dismissed = false;
		}
		if (owner is null || _dismissed)
			return;

		var value = ToolTipService.GetToolTip(owner);
		if (_tip is not null)
		{
			if (!ReferenceEquals(value, _shownValue))
				Hide();
			else
				return;
		}
		_rested += deltaSeconds;
		if (_rested >= ToolTipService.InitialShowDelay)
			Show(owner, value!, pointer);
	}

	// The element the pointer is over that has a tooltip: the leaf or the nearest above it, in a live tree.
	private static UIElement? Owner(UIElement? leaf)
	{
		for (var element = leaf; element is not null; element = element.Parent)
		{
			if (element.AttachedRoot is null)
				return null;
			if (ToolTipService.GetToolTip(element) is not null)
				return element.Visibility == Visibility.Visible ? element : null;
		}
		return null;
	}

	private void Show(UIElement owner, object value, Vector2 pointerPixels)
	{
		if (owner.AttachedRoot is not { } root)
			return;
		var tip = value as ToolTip ?? new ToolTip { Content = value };
		root.AddOverlay(tip, Vector2.Zero);

		var scale = root.LayoutScale > 0f ? root.LayoutScale : 1f;
		var bounds = new Size(root.Visual.SurfaceSize.X / scale, root.Visual.SurfaceSize.Y / scale);
		tip.Measure(bounds);
		var anchor = owner.TransformToVisual(null).TransformBounds(new Rect(0f, 0f, owner.LayoutRect.Width, owner.LayoutRect.Height));
		var position = AnchoredPlacement.Place(anchor, tip.DesiredSize, bounds, ToolTipService.GetPlacement(owner), Gap, pointerPixels / scale);
		root.MoveOverlay(tip, position);

		_tip = tip;
		_root = root;
		_shownValue = value;
	}

	private void Hide()
	{
		if (_tip is not null)
			_root?.RemoveOverlay(_tip);
		_tip = null;
		_root = null;
		_shownValue = null;
	}
}
