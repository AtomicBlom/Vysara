using SUIsei.Input;

namespace SUIsei.Controls;

/// <summary>
/// A clipping viewport over a single oversized <see cref="Content"/> (PlanningUI.md MU6). Measures the content
/// unbounded in the scroll axis to learn its extent, arranges it offset by the scroll position, and clips to the
/// viewport. Scrolls by mouse wheel, by keyboard (Page/Home/End/arrows when a descendant is focused), and
/// programmatically; the offset is always clamped to <see cref="ScrollableHeight"/>/<see cref="ScrollableWidth"/>.
/// Pairs with <see cref="VirtualizingStackPanel"/>, which reports its full extent for exactly this viewport.
/// </summary>
[ContentProperty(Name = nameof(Content))]
public sealed class ScrollViewer : FrameworkElement
{
	public ScrollViewer()
	{
		ClipToBounds = true;
		AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged);
		AddHandler(KeyDownEvent, OnKeyDown);
	}

	public UIElement? Content
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			if (field is not null)
				DropChild(field);
			field = value;
			if (value is not null)
				AdoptChild(value);
			InvalidateMeasure();
		}
	}

	/// <summary>Pixels scrolled per unit of wheel delta (and per arrow-key press).</summary>
	public float WheelScrollAmount { get; set; } = 48f;

	public float VerticalOffset { get; private set; }
	public float HorizontalOffset { get; private set; }
	public float ExtentHeight { get; private set; }
	public float ExtentWidth { get; private set; }
	public float ViewportHeight { get; private set; }
	public float ViewportWidth { get; private set; }

	public float ScrollableHeight => MathF.Max(0f, ExtentHeight - ViewportHeight);
	public float ScrollableWidth => MathF.Max(0f, ExtentWidth - ViewportWidth);

	public void ScrollToVerticalOffset(float offset) => SetVerticalOffset(offset);

	public void ScrollToHorizontalOffset(float offset) => SetHorizontalOffset(offset);

	private void SetVerticalOffset(float offset)
	{
		var clamped = Math.Clamp(offset, 0f, ScrollableHeight);
		if (clamped == VerticalOffset)
			return;
		VerticalOffset = clamped;
		InvalidateArrange();
	}

	private void SetHorizontalOffset(float offset)
	{
		var clamped = Math.Clamp(offset, 0f, ScrollableWidth);
		if (clamped == HorizontalOffset)
			return;
		HorizontalOffset = clamped;
		InvalidateArrange();
	}

	internal override IReadOnlyList<UIElement> VisualChildren => Content is null ? [] : [Content];

	protected override Size MeasureOverride(Size available)
	{
		if (Content is null)
		{
			ExtentWidth = ExtentHeight = 0f;
			return Size.Zero;
		}

		// Measure the content unbounded in the scroll axis so its full extent is known regardless of the viewport.
		Content.Measure(new Size(available.Width, float.PositiveInfinity));
		ExtentWidth = Content.DesiredSize.Width;
		ExtentHeight = Content.DesiredSize.Height;

		// The viewport is the available size; an unbounded container collapses the viewport to the extent (no scroll).
		var width = float.IsPositiveInfinity(available.Width) ? ExtentWidth : available.Width;
		var height = float.IsPositiveInfinity(available.Height) ? ExtentHeight : available.Height;
		return new Size(width, height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		ViewportWidth = finalSize.Width;
		ViewportHeight = finalSize.Height;
		VerticalOffset = Math.Clamp(VerticalOffset, 0f, ScrollableHeight);
		HorizontalOffset = Math.Clamp(HorizontalOffset, 0f, ScrollableWidth);

		if (Content is { } content)
		{
			var contentWidth = MathF.Max(ExtentWidth, finalSize.Width);
			var contentHeight = MathF.Max(ExtentHeight, finalSize.Height);
			content.Arrange(new Rect(-HorizontalOffset, -VerticalOffset, contentWidth, contentHeight));
		}
		return finalSize;
	}

	private void OnPointerWheelChanged(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs pointer)
			return;
		// Wheel up (positive delta) scrolls toward the top (offset decreases).
		SetVerticalOffset(VerticalOffset - pointer.Wheel.Y * WheelScrollAmount);
		e.Handled = true;
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key)
			return;
		switch (key.Key)
		{
			case VirtualKey.PageDown: SetVerticalOffset(VerticalOffset + ViewportHeight); e.Handled = true; break;
			case VirtualKey.PageUp: SetVerticalOffset(VerticalOffset - ViewportHeight); e.Handled = true; break;
			case VirtualKey.Home: SetVerticalOffset(0f); e.Handled = true; break;
			case VirtualKey.End: SetVerticalOffset(ScrollableHeight); e.Handled = true; break;
			case VirtualKey.Down: SetVerticalOffset(VerticalOffset + WheelScrollAmount); e.Handled = true; break;
			case VirtualKey.Up: SetVerticalOffset(VerticalOffset - WheelScrollAmount); e.Handled = true; break;
		}
	}
}
