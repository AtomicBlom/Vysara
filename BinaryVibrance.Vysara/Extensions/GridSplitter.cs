using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.Extensions;

/// <summary>
/// A bar in a <see cref="Grid"/> that resizes the tracks either side of it as it is dragged, as the Windows Community
/// Toolkit's <c>GridSplitter</c> (WinUI has none, so it is <c>&lt;vysara:GridSplitter&gt;</c>). Placed in a row of its
/// own (a thin Auto or pixel row) it resizes the rows above and below; in a column, the columns left and right.
/// <see cref="ResizeDirection"/> says which where its shape doesn't. A primary drag moves the line between the two
/// tracks, each kept no smaller than <see cref="MinimumTrackSize"/>: a pixel or Auto track becomes a pixel track of the
/// size the drag leaves it, while a star track stays a star (beside a fixed track it is left as it is and takes up the
/// rest; two stars are reweighted), so the grid still fits the space it is given. <see cref="Resized"/> says so as it
/// ends, and a drag whose capture is lost ends there.
/// The bar paints the control's <see cref="Control.Background"/> (a faint line unless set), with a grip while the pointer
/// is over it or dragging it.
/// </summary>
public sealed class GridSplitter : Control
{
	private static readonly SolidColorBrush Rest = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 16));
	private static readonly SolidColorBrush Grip = Brush.Sealed(HdrColor.FromRgb(255, 255, 255, 60));

	public static readonly DependencyProperty<GridSplitter, GridResizeDirection> ResizeDirectionProperty =
		DependencyProperty.Register(nameof(ResizeDirection), new PropertyMetadata<GridSplitter, GridResizeDirection>());

	public static readonly DependencyProperty<GridSplitter, float> MinimumTrackSizeProperty =
		DependencyProperty.Register(nameof(MinimumTrackSize), new PropertyMetadata<GridSplitter, float> { DefaultValue = 24f });

	private (bool Rows, int Before, int After, float SizeBefore, float SizeAfter, float Start)? _drag;
	private bool _over;

	public GridSplitter()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		SetBuiltInValue(BackgroundProperty, Rest);
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		AddHandler(PointerEnteredEvent, (_, _) => Hover(true));
		AddHandler(PointerExitedEvent, (_, _) => Hover(false));
		AddHandler(PointerPressedEvent, OnPressed);
		AddHandler(PointerMovedEvent, OnMoved);
		AddHandler(PointerReleasedEvent, OnReleased);
		AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag());
	}

	/// <summary>Which tracks it resizes: worked out from its shape, or rows or columns.</summary>
	public GridResizeDirection ResizeDirection { get => GetValue(ResizeDirectionProperty); set => SetValue(ResizeDirectionProperty, value); }

	/// <summary>The least a track it resizes is left with, in DIPs. 24 by default.</summary>
	public float MinimumTrackSize { get => GetValue(MinimumTrackSizeProperty); set => SetValue(MinimumTrackSizeProperty, value); }

	/// <summary>Raised as a drag ends, with the tracks at their new sizes.</summary>
	public event EventHandler? Resized;

	private bool ResizesRows => ResizeDirection switch
	{
		GridResizeDirection.Rows => true,
		GridResizeDirection.Columns => false,
		_ => LayoutRect.Width >= LayoutRect.Height,
	};

	private protected override string CommonVisualState =>
		!IsEnabled ? "Disabled" : _drag is not null ? "Pressed" : _over ? "PointerOver" : "Normal";

	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		_over = false;
		_drag = null;
	}

	private void Hover(bool over)
	{
		_over = over;
		GoToCommonState(useTransitions: true);
	}

	private void OnPressed(object sender, RoutedEventArgs e)
	{
		if (Parent is not Grid grid || e is not PointerRoutedEventArgs { IsSecondaryButton: false } pointer)
			return;
		var rows = ResizesRows;
		var at = rows ? Grid.GetRow(this) : Grid.GetColumn(this);
		var count = rows ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
		if (at - 1 < 0 || at + 1 >= count)
			return;
		var point = pointer.GetCurrentPoint(grid).Position;
		_drag = (rows, at - 1, at + 1, grid.TrackSize(rows, at - 1), grid.TrackSize(rows, at + 1), rows ? point.Y : point.X);
		CapturePointer(pointer.PointerId);
		GoToCommonState(useTransitions: true);
		e.Handled = true;
	}

	private void OnMoved(object sender, RoutedEventArgs e)
	{
		if (_drag is not { } drag || Parent is not Grid grid || e is not PointerRoutedEventArgs pointer)
			return;
		var point = pointer.GetCurrentPoint(grid).Position;
		var total = drag.SizeBefore + drag.SizeAfter;
		var least = MinimumTrackSize;
		var before = Math.Clamp(drag.SizeBefore + (drag.Rows ? point.Y : point.X) - drag.Start, least, Math.Max(least, total - least));
		var after = total - before;
		var tracks = drag.Rows ? grid.RowDefinitions : grid.ColumnDefinitions;
		// A star track stays a star, so the grid still flows as it is resized: beside a fixed track only the fixed one is
		// set, and it takes the star's place's change; two stars are weighted to the sizes the drag leaves them, their
		// weights summing as they did
		var (wasBefore, wasAfter) = (tracks[drag.Before], tracks[drag.After]);
		if (wasBefore.IsStar && wasAfter.IsStar)
		{
			var weight = wasBefore.Value + wasAfter.Value;
			tracks[drag.Before] = GridLength.Stars(weight * before / total);
			tracks[drag.After] = GridLength.Stars(weight * after / total);
		}
		else if (wasBefore.IsStar)
			tracks[drag.After] = GridLength.Pixels(after);
		else if (wasAfter.IsStar)
			tracks[drag.Before] = GridLength.Pixels(before);
		else
		{
			tracks[drag.Before] = GridLength.Pixels(before);
			tracks[drag.After] = GridLength.Pixels(after);
		}
		grid.InvalidateMeasure();
		e.Handled = true;
	}

	private void OnReleased(object sender, RoutedEventArgs e)
	{
		if (_drag is null || e is not PointerRoutedEventArgs { IsSecondaryButton: false } pointer)
			return;
		e.Handled = true;
		ReleasePointerCapture(pointer.PointerId);
		EndDrag();
	}

	// The drag is over, by a release or a capture lost: the tracks keep the sizes it left them
	private void EndDrag()
	{
		if (_drag is null)
			return;
		_drag = null;
		GoToCommonState(useTransitions: true);
		Resized?.Invoke(this, EventArgs.Empty);
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var bar = new Border();
			control.RegisterTemplateBinding(BackgroundProperty, bar, Border.BackgroundProperty);
			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal" });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = TemplateStates.Background(bar, Grip) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = TemplateStates.Background(bar, Grip) });
			common.States.Add(new VisualState { Name = "Disabled" });
			VisualStateManager.SetVisualStateGroups(bar, [common]);
			return bar;
		},
	};
}
