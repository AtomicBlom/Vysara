using System.Numerics;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A transient overlay: when <see cref="IsOpen"/> is set, its <see cref="Child"/> is composited
/// above the root's content at (<see cref="HorizontalOffset"/>, <see cref="VerticalOffset"/>) and hit-tested on top.
/// The popup element itself occupies no layout space. With <see cref="IsLightDismissEnabled"/> a full-surface layer
/// behind the child closes the popup on an outside press. The popup must be attached to a live root (in the element
/// tree) for <see cref="IsOpen"/> to take effect; <see cref="Controls.MenuFlyout"/> drives the overlay imperatively.
/// </summary>
public sealed class Popup : FrameworkElement
{
	public static readonly DependencyProperty<Popup, bool> IsOpenProperty =
		DependencyProperty.Register(nameof(IsOpen),
			new PropertyMetadata<Popup, bool> { DefaultValue = false, PropertyChanged = static (p, _, open) => p.OnIsOpenChanged(open) });

	private Border? _dismissLayer;
	private bool _shown;

	/// <summary>The element shown in the overlay while open.</summary>
	public UIElement? Child { get; set; }

	/// <summary>When true (default), an outside press closes the popup.</summary>
	public bool IsLightDismissEnabled { get; set; } = true;

	public float HorizontalOffset { get; set; }
	public float VerticalOffset { get; set; }

	public bool IsOpen
	{
		get => GetValue(IsOpenProperty);
		set => SetValue(IsOpenProperty, value);
	}

	public event EventHandler? Opened;
	public event EventHandler? Closed;

	protected override Size MeasureOverride(Size available) => Size.Zero;

	private void OnIsOpenChanged(bool open)
	{
		if (open)
			Show();
		else
			Hide();
	}

	private void Show()
	{
		if (_shown || Child is null || AttachedRoot is not { } root)
			return;

		if (IsLightDismissEnabled)
		{
			_dismissLayer = new Border { Background = HdrColor.Transparent };
			_dismissLayer.AddHandler(PointerPressedEvent, OnDismissPressed);
			root.AddOverlay(_dismissLayer, Vector2.Zero, fullSurface: true);
		}
		root.AddOverlay(Child, new Vector2(HorizontalOffset, VerticalOffset));
		_shown = true;
		Opened?.Invoke(this, EventArgs.Empty);
	}

	private void Hide()
	{
		if (!_shown)
			return;
		if (AttachedRoot is { } root)
		{
			if (Child is not null)
				root.RemoveOverlay(Child);
			if (_dismissLayer is not null)
				root.RemoveOverlay(_dismissLayer);
		}
		_dismissLayer = null;
		_shown = false;
		Closed?.Invoke(this, EventArgs.Empty);
	}

	private void OnDismissPressed(object sender, RoutedEventArgs e)
	{
		IsOpen = false;
		e.Handled = true;
	}
}
