using System.Globalization;
using SUIsei.Controls.Primitives;
using SUIsei.Input;
using SUIsei.Media;
using SUIsei.RoutedEvents;

namespace SUIsei.Controls;

/// <summary>
/// A number typed or stepped, as WinUI's <c>NumberBox</c>: a text box showing <see cref="Value"/>, which takes what is
/// typed as Enter is pressed or focus leaves (a number in the current culture or the invariant one; anything else puts
/// the value back), clamped to <see cref="Minimum"/> and <see cref="Maximum"/>. Up and Down step it by
/// <see cref="SmallChange"/>, Page Up and Page Down by <see cref="LargeChange"/>, and so does the wheel while it has
/// focus; the spin buttons show where <see cref="SpinButtonPlacementMode"/> asks. NaN is no value, shown empty.
/// <see cref="ValueChanged"/> lets it bind TwoWay. The value is shown in full, with at least as many decimals as
/// <see cref="SmallChange"/> has (up to four), so 0.25 stepped by 1 shows as 0.25 and 3 stepped by 0.1 as 3.0.
/// </summary>
public sealed class NumberBox : Control
{
	private static readonly SolidColorBrush SpinStroke = Brush.Sealed(HdrColor.FromRgb(170, 176, 190));
	private static readonly SolidColorBrush Placeholder = Brush.Sealed(HdrColor.FromRgb(140, 146, 160));

	public static readonly DependencyProperty<NumberBox, double> ValueProperty =
		DependencyProperty.Register(nameof(Value),
			new PropertyMetadata<NumberBox, double> { DefaultValue = double.NaN, PropertyChanged = static (n, old, _) => n.OnValueChanged(old) });

	public static readonly DependencyProperty<NumberBox, double> MinimumProperty =
		DependencyProperty.Register(nameof(Minimum),
			new PropertyMetadata<NumberBox, double> { DefaultValue = double.MinValue, PropertyChanged = static (n, _, _) => n.Value = n.Clamp(n.Value) });

	public static readonly DependencyProperty<NumberBox, double> MaximumProperty =
		DependencyProperty.Register(nameof(Maximum),
			new PropertyMetadata<NumberBox, double> { DefaultValue = double.MaxValue, PropertyChanged = static (n, _, _) => n.Value = n.Clamp(n.Value) });

	public static readonly DependencyProperty<NumberBox, double> SmallChangeProperty =
		DependencyProperty.Register(nameof(SmallChange),
			new PropertyMetadata<NumberBox, double> { DefaultValue = 1d, PropertyChanged = static (n, _, _) => n.ShowValue() });

	public static readonly DependencyProperty<NumberBox, double> LargeChangeProperty =
		DependencyProperty.Register(nameof(LargeChange), new PropertyMetadata<NumberBox, double> { DefaultValue = 10d });

	public static readonly DependencyProperty<NumberBox, NumberBoxSpinButtonPlacementMode> SpinButtonPlacementModeProperty =
		DependencyProperty.Register(nameof(SpinButtonPlacementMode),
			new PropertyMetadata<NumberBox, NumberBoxSpinButtonPlacementMode> { PropertyChanged = static (n, _, _) => n.ShowSpin() });

	public static readonly DependencyProperty<NumberBox, object?> HeaderProperty =
		DependencyProperty.Register(nameof(Header),
			new PropertyMetadata<NumberBox, object?> { PropertyChanged = static (n, _, _) => n.ShowHeader() });

	public static readonly DependencyProperty<NumberBox, string?> PlaceholderTextProperty =
		DependencyProperty.Register(nameof(PlaceholderText),
			new PropertyMetadata<NumberBox, string?> { PropertyChanged = static (n, _, _) => n.ShowPlaceholder() });

	private TextBox? _text;
	private StackPanel? _spin;
	private ContentPresenter? _header;
	private TextBlock? _placeholder;
	private bool _showing;
	// The text last shown for the value, so a commit of text nobody changed leaves the value alone
	private string _shownText = "";

	public NumberBox()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		SetBuiltInValue(TemplateProperty, DefaultTemplate);
		AddHandler(PointerWheelChangedEvent, OnWheel);
	}

	/// <summary>The number, or NaN for none.</summary>
	public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

	/// <summary>The least the value may be.</summary>
	public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

	/// <summary>The most the value may be.</summary>
	public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

	/// <summary>The step of Up, Down and a wheel notch. 1 by default.</summary>
	public double SmallChange { get => GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }

	/// <summary>The step of Page Up and Page Down. 10 by default.</summary>
	public double LargeChange { get => GetValue(LargeChangeProperty); set => SetValue(LargeChangeProperty, value); }

	/// <summary>Where the up and down buttons show. Hidden by default.</summary>
	public NumberBoxSpinButtonPlacementMode SpinButtonPlacementMode
	{
		get => GetValue(SpinButtonPlacementModeProperty);
		set => SetValue(SpinButtonPlacementModeProperty, value);
	}

	/// <summary>Shown above the box, or nothing where null.</summary>
	public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

	/// <summary>Shown in the box, dimmed, while it holds no value.</summary>
	public string? PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

	/// <summary>Raised as <see cref="Value"/> changes, so it binds TwoWay.</summary>
	public event TypedEventHandler<NumberBox, NumberBoxValueChangedEventArgs>? ValueChanged;

	// Steps the value by `steps` of `change`; from no value, starts at 0 clamped
	internal void Step(double steps, double change) => Value = Clamp((double.IsNaN(Value) ? 0d : Value) + steps * change);

	private double Clamp(double value) => double.IsNaN(value) ? value : Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));

	// Clamping writes the value back, as WinUI's NumberBox does, so a TwoWay source is given the value shown
	private void OnValueChanged(double old)
	{
		var clamped = Clamp(Value);
		if (!clamped.Equals(Value))
		{
			Value = clamped;
			return;
		}
		ShowValue();
		ValueChanged?.Invoke(this, new NumberBoxValueChangedEventArgs(old, Value));
	}

	// What is typed taken as the value, or the value shown again. Text that is still what was shown changes nothing, so
	// focus passing through never rounds the value to what it shows.
	private void Commit()
	{
		if (_text is null)
			return;
		var text = _text.Text.Trim();
		if (text == _shownText)
			return;
		if (text.Length == 0)
			Value = double.NaN;
		else if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed)
			|| double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out typed))
			Value = Clamp(typed);
		ShowValue();
	}

	private void ShowValue()
	{
		if (_showing || _text is null)
			return;
		_showing = true;
		try
		{
			_shownText = Format(Value, SmallChange);
			_text.Text = _shownText;
		}
		finally
		{
			_showing = false;
		}
		ShowPlaceholder();
	}

	/// <summary>How a value shows: in full, round-tripping, with at least the step's decimals.</summary>
	internal static string Format(double value, double step)
	{
		if (double.IsNaN(value))
			return "";
		var decimals = Decimals(step);
		var format = decimals == 0 ? "0.###############" : "0." + new string('0', decimals) + new string('#', 15 - decimals);
		var shown = value.ToString(format, CultureInfo.CurrentCulture);
		// Past fifteen significant digits the custom format rounds; fall back to the round-trip form there
		return double.Parse(shown, CultureInfo.CurrentCulture).Equals(value) ? shown : value.ToString("R", CultureInfo.CurrentCulture);
	}

	private void ShowSpin()
	{
		if (_spin is not null)
			_spin.Visibility = SpinButtonPlacementMode == NumberBoxSpinButtonPlacementMode.Hidden ? Visibility.Collapsed : Visibility.Visible;
	}

	private void ShowHeader()
	{
		if (_header is null)
			return;
		_header.Content = Header;
		_header.Visibility = Header is null ? Visibility.Collapsed : Visibility.Visible;
	}

	// The placeholder shows over the box while it is empty
	private void ShowPlaceholder()
	{
		if (_text is null || _placeholder is null)
			return;
		_placeholder.Text = PlaceholderText ?? "";
		_placeholder.Visibility = _text.Text.Length == 0 && !string.IsNullOrEmpty(PlaceholderText) ? Visibility.Visible : Visibility.Collapsed;
	}

	// How many decimals a step has, so a value stepped by it shows them all
	private static int Decimals(double step)
	{
		for (var d = 0; d < 4; d++)
		{
			var scaled = step * Math.Pow(10, d);
			if (Math.Abs(scaled - Math.Round(scaled)) < 1e-9)
				return d;
		}
		return 4;
	}

	private void OnPreviewKey(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key)
			return;
		switch (key.Key)
		{
			case VirtualKey.Enter: Commit(); break;
			case VirtualKey.Up: Commit(); Step(1, SmallChange); break;
			case VirtualKey.Down: Commit(); Step(-1, SmallChange); break;
			case VirtualKey.PageUp: Commit(); Step(1, LargeChange); break;
			case VirtualKey.PageDown: Commit(); Step(-1, LargeChange); break;
			default: return;
		}
		e.Handled = true;
	}

	// The wheel steps the value only while the box has focus, as WinUI's does, so a page scrolled past it never changes it
	private void OnWheel(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs { Wheel.Y: var notch and not 0f } || _text is null
			|| !ReferenceEquals(AttachedRoot?.FocusManager?.FocusedElement, _text))
			return;
		Commit();
		Step(MathF.Sign(notch), SmallChange);
		e.Handled = true;
	}

	private static Button Spin(NumberBox box, UIElement mark, int direction, string tip)
	{
		var button = new Button { Content = mark, Padding = new Thickness(6f, 1f, 6f, 1f), IsTabStop = false };
		ToolTipService.SetToolTip(button, tip);
		button.Click += (_, _) =>
		{
			box.Commit();
			box.Step(direction, box.SmallChange);
		};
		return button;
	}

	private static readonly ControlTemplate DefaultTemplate = new()
	{
		Factory = static control =>
		{
			var self = (NumberBox)control;
			var text = new TextBox();
			var spin = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4f, 0f, 0f, 0f) };
			var header = new ContentPresenter { Margin = new Thickness(0f, 0f, 0f, 6f), Visibility = Visibility.Collapsed };
			self.RegisterTemplateBinding(ForegroundProperty, header, ContentPresenter.ForegroundProperty);
			var row = new Grid();
			row.ColumnDefinitions.Add(GridLength.Star);
			row.ColumnDefinitions.Add(GridLength.Auto);
			// Where the text box draws its text, dimmed, not taking the pointer
			var placeholder = new TextBlock
			{
				Foreground = Placeholder,
				FontSize = 14f,
				Margin = new Thickness(4f, 2f, 4f, 2f),
				IsHitTestVisible = false,
				Visibility = Visibility.Collapsed,
			};
			Grid.SetColumn(spin, 1);
			row.Children.Add(text);
			row.Children.Add(placeholder);
			row.Children.Add(spin);
			spin.Children.Add(Spin(self, Marks.ChevronUp(8f, SpinStroke), 1, "Increase"));
			spin.Children.Add(Spin(self, Marks.ChevronDown(8f, SpinStroke), -1, "Decrease"));
			var column = new StackPanel { Orientation = Orientation.Vertical };
			column.Children.Add(header);
			column.Children.Add(row);
			text.AddHandler(PreviewKeyDownEvent, self.OnPreviewKey);
			text.AddHandler(LostFocusEvent, (_, _) => self.Commit());
			text.TextChanged += (_, _) => self.ShowPlaceholder();
			(self._text, self._spin, self._header, self._placeholder) = (text, spin, header, placeholder);
			self.ShowValue();
			self.ShowSpin();
			self.ShowHeader();
			return column;
		},
	};
}
