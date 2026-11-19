using System.Numerics;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls.Primitives;

/// <summary>
/// What a flyout is, as WinUI's <c>FlyoutBase</c>: something opened over the page against a target, by
/// <see cref="ShowAt(FrameworkElement)"/>, a <see cref="Button.Flyout"/>'s click or an element's
/// <see cref="UIElement.ContextFlyout"/>. Its presenter goes in the target's root's overlay layer, on
/// <see cref="Placement"/>'s side of the target (turned to the other side where there is no room, slid along to stay on
/// the surface, and no taller or wider than the room on its side, a presenter that scrolls scrolling), over a
/// light-dismiss layer: a press outside closes it, as does Escape. Opening it focuses the first tab stop inside, and
/// closing it gives focus back to what had it. One shows per flyout; opening it again moves it.
/// </summary>
public abstract class FlyoutBase : DependencyObject
{
	// The least a flyout is ever squeezed to, and how far it keeps from the surface's edge on its side, in DIPs.
	private const float EdgeMargin = 8f;
	private const float MinExtent = 48f;

	private static readonly SolidColorBrush DismissBackground = Brush.Sealed(HdrColor.Transparent);

	private UIRoot? _root;
	private FrameworkElement? _presenter;
	private Border? _dismissLayer;
	private UIElement? _restoreFocus;

	/// <summary>Which side of its target the flyout opens on, as WinUI's.</summary>
	public FlyoutPlacementMode Placement { get; set; } = FlyoutPlacementMode.Top;

	/// <summary>Whether the flyout is showing.</summary>
	public bool IsOpen => _root is not null;

	/// <summary>The element the flyout was last shown against.</summary>
	public UIElement? Target { get; private set; }

	/// <summary>Raised as the flyout has opened.</summary>
	public event EventHandler<object>? Opened;

	/// <summary>Raised as the flyout has closed: by <see cref="Hide"/>, a press outside, Escape, or what it holds.</summary>
	public event EventHandler<object>? Closed;

	/// <summary>Opens the flyout against <paramref name="placementTarget"/>, which must be in a live tree.</summary>
	public void ShowAt(FrameworkElement placementTarget) => ShowAt(placementTarget, null);

	/// <summary>Opens the flyout against <paramref name="placementTarget"/>, or at a point in it, as
	/// <paramref name="options"/> says.</summary>
	public void ShowAt(UIElement placementTarget, FlyoutShowOptions? options)
	{
		ArgumentNullException.ThrowIfNull(placementTarget);
		if (placementTarget.AttachedRoot is not { } root)
			return;
		Hide();

		var (side, align) = Sides(options?.Placement ?? Placement);
		var toRoot = placementTarget.TransformToVisual(null);
		var anchor = options?.Position is { } point
			? new Rect(toRoot.TransformPoint(point).X, toRoot.TransformPoint(point).Y, 0f, 0f)
			: toRoot.TransformBounds(new Rect(0f, 0f, placementTarget.LayoutRect.Width, placementTarget.LayoutRect.Height));
		if (options?.Position is not null)
			align = EdgeAlignment.Start;

		Target = placementTarget;
		var presenter = CreatePresenter();
		presenter.AddHandler(UIElement.KeyDownEvent, OnPresenterKeyDown);
		_dismissLayer = new Border { Background = DismissBackground };
		_dismissLayer.AddHandler(UIElement.PointerPressedEvent, OnDismissPressed);
		root.AddOverlay(_dismissLayer, Vector2.Zero, fullSurface: true);
		root.AddOverlay(presenter, Vector2.Zero);

		// Measured against the surface, held to the room on the side it opens on, placed, and arranged there at once
		var scale = root.LayoutScale > 0f ? root.LayoutScale : 1f;
		var bounds = new Size(root.Visual.SurfaceSize.X / scale, root.Visual.SurfaceSize.Y / scale);
		if (bounds.Width > 0f && bounds.Height > 0f)
		{
			var (room, opposite) = AnchoredPlacement.Room(anchor, bounds, side, Gap);
			var most = MathF.Max(MinExtent, MathF.Max(room, opposite) - EdgeMargin);
			if (side is PlacementMode.Top or PlacementMode.Bottom)
				presenter.MaxHeight = MathF.Min(presenter.MaxHeight, most);
			else
				presenter.MaxWidth = MathF.Min(presenter.MaxWidth, most);
			presenter.Measure(bounds);
			root.MoveOverlay(presenter, AnchoredPlacement.Place(anchor, presenter.DesiredSize, bounds, side, Gap, align: align));
		}

		_root = root;
		_presenter = presenter;
		_restoreFocus = root.FocusManager?.FocusedElement;
		if (FirstTabStop(presenter) is { } first)
			first.Focus(FocusState.Programmatic);
		OnOpened();
		Opened?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Closes the flyout, if it is open.</summary>
	public void Hide()
	{
		if (_root is not { } root)
			return;
		if (_presenter is not null)
			root.RemoveOverlay(_presenter);
		if (_dismissLayer is not null)
			root.RemoveOverlay(_dismissLayer);
		var restore = _restoreFocus;
		_root = null;
		_presenter = null;
		_dismissLayer = null;
		_restoreFocus = null;
		if (restore is { AttachedRoot: not null } && FocusManager.IsFocusable(restore))
			restore.Focus(FocusState.Programmatic);
		OnClosed();
		Closed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The gap between the flyout and its target, in DIPs.</summary>
	protected virtual float Gap => 4f;

	/// <summary>Makes the element shown while the flyout is open, afresh each time it opens.</summary>
	protected abstract FrameworkElement CreatePresenter();

	/// <summary>The flyout has opened, its presenter shown.</summary>
	protected virtual void OnOpened()
	{
	}

	/// <summary>The flyout has closed, its presenter gone.</summary>
	protected virtual void OnClosed()
	{
	}

	/// <summary>The brush under <paramref name="key"/> from the target up, or <paramref name="fallback"/>: what a presenter
	/// is coloured by, as a built-in template reads a theme resource.</summary>
	protected Brush Resource(UIElement target, string key, Brush fallback)
	{
		for (var element = target; element is not null; element = element.Parent)
		{
			if (element is FrameworkElement { Resources: { } resources } && resources.TryFind(key, out var found) && found is Brush brush)
				return brush;
		}
		return fallback;
	}

	private static (PlacementMode Side, EdgeAlignment Align) Sides(FlyoutPlacementMode placement) => placement switch
	{
		FlyoutPlacementMode.Top => (PlacementMode.Top, EdgeAlignment.Center),
		FlyoutPlacementMode.Bottom or FlyoutPlacementMode.Auto => (PlacementMode.Bottom, EdgeAlignment.Center),
		FlyoutPlacementMode.Left => (PlacementMode.Left, EdgeAlignment.Center),
		FlyoutPlacementMode.Right => (PlacementMode.Right, EdgeAlignment.Center),
		FlyoutPlacementMode.TopEdgeAlignedLeft => (PlacementMode.Top, EdgeAlignment.Start),
		FlyoutPlacementMode.TopEdgeAlignedRight => (PlacementMode.Top, EdgeAlignment.End),
		FlyoutPlacementMode.BottomEdgeAlignedLeft => (PlacementMode.Bottom, EdgeAlignment.Start),
		FlyoutPlacementMode.BottomEdgeAlignedRight => (PlacementMode.Bottom, EdgeAlignment.End),
		FlyoutPlacementMode.LeftEdgeAlignedTop => (PlacementMode.Left, EdgeAlignment.Start),
		FlyoutPlacementMode.LeftEdgeAlignedBottom => (PlacementMode.Left, EdgeAlignment.End),
		FlyoutPlacementMode.RightEdgeAlignedTop => (PlacementMode.Right, EdgeAlignment.Start),
		FlyoutPlacementMode.RightEdgeAlignedBottom => (PlacementMode.Right, EdgeAlignment.End),
		_ => throw new NotSupportedException($"FlyoutPlacementMode.{placement} is not provided: open the flyout on a side of its target"),
	};

	// The first element inside that takes focus, depth first
	private static UIElement? FirstTabStop(UIElement element)
	{
		if (FocusManager.IsFocusable(element))
			return element;
		foreach (var child in element.VisualChildren)
		{
			if (FirstTabStop(child) is { } found)
				return found;
		}
		return null;
	}

	private void OnPresenterKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is KeyRoutedEventArgs { Key: VirtualKey.Escape or VirtualKey.GamepadB })
		{
			Hide();
			e.Handled = true;
		}
	}

	private void OnDismissPressed(object sender, RoutedEventArgs e)
	{
		Hide();
		e.Handled = true;
	}
}
