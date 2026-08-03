using System.Numerics;
using SUIsei.Animation;
using SUIsei.Input;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A range control that picks a <see cref="Value"/> in <c>[<see cref="Minimum"/>, <see cref="Maximum"/>]</c> by
/// dragging a thumb along a track, clicking the track, or with the keyboard. The default
/// template draws an unfilled rail, an accent fill up to the thumb, and a draggable thumb, and carries a
/// <c>CommonStates</c> visual-state group (<c>Normal</c>/<c>PointerOver</c>/<c>Pressed</c>/<c>Disabled</c>) that the
/// pointer drives. <see cref="Value"/> is coerced into range and snapped to <see cref="StepFrequency"/> on input;
/// <see cref="ValueChanged"/> lets a <c>{x:Bind Value, Mode=TwoWay}</c> push edits back to a view model.
/// </summary>
public sealed class Slider : Control
{
	private const float ThumbDiameter = 18f;
	private const float RailThickness = 4f;
	private const float DefaultLength = 132f;

	private static readonly HdrColor RailColor = HdrColor.FromRgb(70, 74, 86);
	private static readonly HdrColor FillColor = HdrColor.FromRgb(76, 140, 224);
	private static readonly HdrColor ThumbBorder = HdrColor.FromRgb(28, 32, 40);
	private static readonly HdrColor ThumbNormal = HdrColor.FromRgb(90, 150, 230);
	private static readonly HdrColor ThumbOver = HdrColor.FromRgb(120, 175, 245);
	private static readonly HdrColor ThumbPressed = HdrColor.FromRgb(150, 195, 255);
	private static readonly HdrColor ThumbDisabled = HdrColor.FromRgb(95, 98, 108);

	private SliderTrack? _track;
	private bool _dragging;
	private bool _pointerOver;

	public static readonly DependencyProperty<Slider, double> MinimumProperty =
		DependencyProperty.Register(nameof(Minimum),
			new PropertyMetadata<Slider, double> { DefaultValue = 0d, PropertyChanged = static (s, _, _) => s.OnRangeChanged() });

	public static readonly DependencyProperty<Slider, double> MaximumProperty =
		DependencyProperty.Register(nameof(Maximum),
			new PropertyMetadata<Slider, double> { DefaultValue = 100d, PropertyChanged = static (s, _, _) => s.OnRangeChanged() });

	public static readonly DependencyProperty<Slider, double> ValueProperty =
		DependencyProperty.Register(nameof(Value),
			new PropertyMetadata<Slider, double>
			{
				DefaultValue = 0d,
				// Coercion runs on every read, so Value is always reported inside the current range even after the
				// range itself moves (Math.Min/Max guards a Minimum mistakenly set above Maximum from throwing).
				Coerce = static (s, v) => Math.Clamp(v, Math.Min(s.Minimum, s.Maximum), Math.Max(s.Minimum, s.Maximum)),
				PropertyChanged = static (s, _, _) => s.OnValueChanged(),
			});

	public Slider() => Template = DefaultTemplate;

	/// <summary>The low end of the range (default 0).</summary>
	public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

	/// <summary>The high end of the range (default 100).</summary>
	public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

	/// <summary>The selected value, coerced into <c>[<see cref="Minimum"/>, <see cref="Maximum"/>]</c>.</summary>
	public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

	/// <summary>The granularity values snap to on pointer/keyboard input (0 disables snapping). Default 1.</summary>
	public double StepFrequency { get; set; } = 1d;

	/// <summary>The change applied by Page Up / Page Down. Default 10.</summary>
	public double LargeChange { get; set; } = 10d;

	/// <summary>Whether the slider runs left-to-right (default) or bottom-to-top.</summary>
	public Orientation Orientation
	{
		get;
		set { if (field != value) { field = value; InvalidateMeasure(); _track?.InvalidateArrange(); } }
	} = Orientation.Horizontal;

	/// <summary>Raised whenever <see cref="Value"/> changes — the <c>ValueChanged</c> hook a <c>Mode=TwoWay</c>
	/// <c>{x:Bind}</c> wires to push edits back to the source.</summary>
	public event EventHandler? ValueChanged;

	/// <summary>The fraction of the range the current value sits at, in <c>[0,1]</c> (0 when the range is empty).</summary>
	internal double Fraction
	{
		get
		{
			var range = Maximum - Minimum;
			return range > 0 ? Math.Clamp((Value - Minimum) / range, 0d, 1d) : 0d;
		}
	}

	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();

		// Pointer + keyboard are handled on the control so a press anywhere on the track (including the thumb) starts
		// a drag; the captured pointer keeps tracking past the track's edges.
		AddHandler(PointerPressedEvent, OnPointerPressed);
		AddHandler(PointerMovedEvent, OnPointerMoved);
		AddHandler(PointerReleasedEvent, OnPointerReleased);
		AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
		AddHandler(PointerEnteredEvent, OnPointerEntered);
		AddHandler(PointerExitedEvent, OnPointerExited);
		AddHandler(KeyDownEvent, OnKeyDown);

		UpdateVisualState();
	}

	private void OnValueChanged()
	{
		_track?.InvalidateArrange(); // re-arranges the fill width + thumb position from the new Fraction
		ValueChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnRangeChanged() => _track?.InvalidateArrange();

	// Snap a raw value to the nearest StepFrequency multiple measured from Minimum, then let the Value coercion clamp it.
	private double Snap(double raw)
	{
		if (StepFrequency <= 0d)
			return raw;
		var steps = Math.Round((raw - Minimum) / StepFrequency);
		return Minimum + steps * StepFrequency;
	}

	// The host DIP→pixel scale (pointer positions arrive in surface pixels; layout is in DIPs).
	private float Scale => AttachedRoot?.LayoutScale is { } s and > 0f ? s : 1f;

	// Map a pointer event to a value: convert its surface-pixel position to the track's local DIP space (summing
	// layout offsets up the parent chain), then to a fraction of the usable travel, then to a snapped value.
	private void SetValueFromPointer(PointerRoutedEventArgs p)
	{
		if (_track is null)
			return;

		var origin = Vector2.Zero;
		for (UIElement? e = _track; e is not null; e = e.Parent)
			origin += new Vector2(e.LayoutRect.X, e.LayoutRect.Y);
		var local = p.Pointer.Position / Scale - origin;

		double fraction;
		if (Orientation == Orientation.Horizontal)
		{
			var travel = MathF.Max(1f, _track.LayoutRect.Width - ThumbDiameter);
			fraction = Math.Clamp((local.X - ThumbDiameter / 2f) / travel, 0f, 1f);
		}
		else
		{
			var travel = MathF.Max(1f, _track.LayoutRect.Height - ThumbDiameter);
			fraction = 1d - Math.Clamp((local.Y - ThumbDiameter / 2f) / travel, 0f, 1f); // value grows upward
		}

		Value = Snap(Minimum + fraction * (Maximum - Minimum));
	}

	private void OnPointerPressed(object sender, RoutedEventArgs e)
	{
		if (!IsEnabled || e is not PointerRoutedEventArgs p)
			return;
		_dragging = true;
		CapturePointer(0);
		SetValueFromPointer(p);
		UpdateVisualState();
		e.Handled = true;
	}

	private void OnPointerMoved(object sender, RoutedEventArgs e)
	{
		if (!_dragging || e is not PointerRoutedEventArgs p)
			return;
		SetValueFromPointer(p);
		e.Handled = true;
	}

	private void OnPointerReleased(object sender, RoutedEventArgs e)
	{
		if (!_dragging)
			return;
		_dragging = false;
		ReleasePointerCapture(0);
		UpdateVisualState();
		e.Handled = true;
	}

	private void OnPointerCaptureLost(object sender, RoutedEventArgs e)
	{
		_dragging = false;
		UpdateVisualState();
	}

	private void OnPointerEntered(object sender, RoutedEventArgs e)
	{
		_pointerOver = true;
		UpdateVisualState();
	}

	private void OnPointerExited(object sender, RoutedEventArgs e)
	{
		_pointerOver = false;
		UpdateVisualState();
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (!IsEnabled || e is not KeyRoutedEventArgs key)
			return;
		var step = StepFrequency > 0d ? StepFrequency : 1d;
		switch (key.Key)
		{
			case VirtualKey.Right or VirtualKey.Up: Value = Snap(Value + step); e.Handled = true; break;
			case VirtualKey.Left or VirtualKey.Down: Value = Snap(Value - step); e.Handled = true; break;
			case VirtualKey.PageUp: Value = Snap(Value + LargeChange); e.Handled = true; break;
			case VirtualKey.PageDown: Value = Snap(Value - LargeChange); e.Handled = true; break;
			case VirtualKey.Home: Value = Minimum; e.Handled = true; break;
			case VirtualKey.End: Value = Maximum; e.Handled = true; break;
		}
	}

	private void UpdateVisualState()
	{
		var state = !IsEnabled ? "Disabled"
			: _dragging ? "Pressed"
			: _pointerOver ? "PointerOver"
			: "Normal";
		VisualStateManager.GoToState(this, state, useTransitions: true);
	}

	// The default look: an unfilled rail, an accent fill up to the thumb, and a circular thumb whose colour the
	// CommonStates group animates. The track panel positions the parts from the slider's Fraction on each arrange.
	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var slider = (Slider)control;

			var rail = new Border { Background = RailColor, CornerRadius = RailThickness / 2f };
			var fill = new Border { Background = FillColor, CornerRadius = RailThickness / 2f };
			var thumb = new Border
			{
				Background = ThumbNormal,
				BorderBrush = ThumbBorder,
				BorderThickness = new Thickness(1f),
				CornerRadius = ThumbDiameter / 2f,
			};

			var track = new SliderTrack(slider);
			track.Children.Add(rail);
			track.Children.Add(fill);
			track.Children.Add(thumb);

			slider._track = track;

			var common = new VisualStateGroup { Name = "CommonStates" };
			common.States.Add(new VisualState { Name = "Normal", Storyboard = ThumbFillTo(thumb, ThumbOver, ThumbNormal) });
			common.States.Add(new VisualState { Name = "PointerOver", Storyboard = ThumbFillTo(thumb, ThumbNormal, ThumbOver) });
			common.States.Add(new VisualState { Name = "Pressed", Storyboard = ThumbFillTo(thumb, ThumbOver, ThumbPressed) });
			common.States.Add(new VisualState { Name = "Disabled", Storyboard = ThumbFillTo(thumb, ThumbNormal, ThumbDisabled) });
			VisualStateManager.SetVisualStateGroups(track, [common]);

			return track;
		},
	};

	private static Storyboard ThumbFillTo(Border thumb, HdrColor from, HdrColor to) => new()
	{
		Children =
		{
			new ColorAnimation
			{
				From = from, To = to, Duration = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.HoldEnd,
				Apply = thumb.SetAnimatedFill, Release = thumb.ClearAnimatedFill,
			},
		},
	};

	/// <summary>The slider's visual: it lays out the rail (full length), the accent fill (up to the thumb), and the
	/// thumb (at the value's fraction of the usable travel) directly from the owning <see cref="Slider"/>, so the
	/// geometry is recomputed on every arrange with no relayout feedback.</summary>
	private sealed class SliderTrack(Slider owner) : Panel
	{
		protected override Size MeasureOverride(Size available)
		{
			foreach (var child in Children.Items)
				child.Measure(available);

			if (owner.Orientation == Orientation.Horizontal)
			{
				var width = float.IsFinite(available.Width) ? available.Width : DefaultLength;
				return new Size(width, ThumbDiameter);
			}
			var height = float.IsFinite(available.Height) ? available.Height : DefaultLength;
			return new Size(ThumbDiameter, height);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			var rail = Children[0];
			var fill = Children[1];
			var thumb = Children[2];
			var fraction = (float)owner.Fraction;

			if (owner.Orientation == Orientation.Horizontal)
			{
				var railY = (finalSize.Height - RailThickness) / 2f;
				var travel = MathF.Max(0f, finalSize.Width - ThumbDiameter);
				var thumbX = fraction * travel;
				var center = thumbX + ThumbDiameter / 2f;
				rail.Arrange(new Rect(0f, railY, finalSize.Width, RailThickness));
				fill.Arrange(new Rect(0f, railY, center, RailThickness));
				thumb.Arrange(new Rect(thumbX, (finalSize.Height - ThumbDiameter) / 2f, ThumbDiameter, ThumbDiameter));
			}
			else
			{
				var railX = (finalSize.Width - RailThickness) / 2f;
				var travel = MathF.Max(0f, finalSize.Height - ThumbDiameter);
				var thumbY = (1f - fraction) * travel; // value 0 at the bottom
				var center = thumbY + ThumbDiameter / 2f;
				rail.Arrange(new Rect(railX, 0f, RailThickness, finalSize.Height));
				fill.Arrange(new Rect(railX, center, RailThickness, finalSize.Height - center));
				thumb.Arrange(new Rect((finalSize.Width - ThumbDiameter) / 2f, thumbY, ThumbDiameter, ThumbDiameter));
			}
			return finalSize;
		}
	}
}
