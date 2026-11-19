using SUIsei.Controls;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Extensions;

/// <summary>
/// A bar in a <see cref="Grid"/> that resizes the tracks either side of it as it is dragged, as the Windows Community
/// Toolkit's <c>GridSplitter</c> (WinUI has none, so it is <c>&lt;suisei:GridSplitter&gt;</c>). Placed in a row of its
/// own (a thin Auto or pixel row) it resizes the rows above and below; in a column, the columns left and right.
/// <see cref="ResizeDirection"/> says which where its shape doesn't. As the drag starts both tracks become pixel tracks
/// of the sizes they had, and they keep the sizes the drag leaves them, each no smaller than
/// <see cref="MinimumTrackSize"/>; <see cref="Resized"/> says so as it ends. It shows a grip while the pointer is over it.
/// </summary>
public sealed class GridSplitter : Control
{
	private static readonly SolidColorBrush Rest = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 16));
	private static readonly SolidColorBrush Grip = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 60));

	private readonly Border _bar = new() { Background = Rest };
	private (bool Rows, int Before, int After, float SizeBefore, float SizeAfter, float Start)? _drag;

	public GridSplitter()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		SetBuiltInValue(TemplateProperty, new ControlTemplate { Factory = static control => ((GridSplitter)control)._bar });
		AddHandler(PointerEnteredEvent, (_, _) => _bar.Background = Grip);
		AddHandler(PointerExitedEvent, (_, _) => _bar.Background = _drag is null ? Rest : Grip);
		AddHandler(PointerPressedEvent, OnPressed);
		AddHandler(PointerMovedEvent, OnMoved);
		AddHandler(PointerReleasedEvent, OnReleased);
	}

	/// <summary>Which tracks it resizes: worked out from its shape, or rows or columns.</summary>
	public GridResizeDirection ResizeDirection { get; set; }

	/// <summary>The least a track it resizes is left with, in DIPs. 24 by default.</summary>
	public float MinimumTrackSize { get; set; } = 24f;

	/// <summary>Raised as a drag ends, with the tracks at their new sizes.</summary>
	public event EventHandler? Resized;

	private bool ResizesRows => ResizeDirection switch
	{
		GridResizeDirection.Rows => true,
		GridResizeDirection.Columns => false,
		_ => LayoutRect.Width >= LayoutRect.Height,
	};

	private void OnPressed(object sender, RoutedEventArgs e)
	{
		if (Parent is not Grid grid || e is not PointerRoutedEventArgs pointer)
			return;
		var rows = ResizesRows;
		var at = rows ? Grid.GetRow(this) : Grid.GetColumn(this);
		var count = rows ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
		if (at - 1 < 0 || at + 1 >= count)
			return;
		var point = pointer.GetCurrentPoint(grid).Position;
		_drag = (rows, at - 1, at + 1, grid.TrackSize(rows, at - 1), grid.TrackSize(rows, at + 1), rows ? point.Y : point.X);
		CapturePointer(pointer.PointerId);
		e.Handled = true;
	}

	private void OnMoved(object sender, RoutedEventArgs e)
	{
		if (_drag is not { } drag || Parent is not Grid grid || e is not PointerRoutedEventArgs pointer)
			return;
		var point = pointer.GetCurrentPoint(grid).Position;
		var total = drag.SizeBefore + drag.SizeAfter;
		var before = Math.Clamp(drag.SizeBefore + (drag.Rows ? point.Y : point.X) - drag.Start, MinimumTrackSize, Math.Max(MinimumTrackSize, total - MinimumTrackSize));
		var tracks = drag.Rows ? grid.RowDefinitions : grid.ColumnDefinitions;
		tracks[drag.Before] = GridLength.Pixels(before);
		tracks[drag.After] = GridLength.Pixels(total - before);
		grid.InvalidateMeasure();
		e.Handled = true;
	}

	private void OnReleased(object sender, RoutedEventArgs e)
	{
		if (_drag is null || e is not PointerRoutedEventArgs pointer)
			return;
		_drag = null;
		ReleasePointerCapture(pointer.PointerId);
		Resized?.Invoke(this, EventArgs.Empty);
		e.Handled = true;
	}
}
