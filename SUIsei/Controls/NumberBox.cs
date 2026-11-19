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
/// <see cref="SmallChange"/>, Page Up and Page Down by <see cref="LargeChange"/>, and so does the wheel over it; the spin
/// buttons show where <see cref="SpinButtonPlacementMode"/> asks. NaN is no value, shown empty. <see cref="ValueChanged"/>
/// lets it bind TwoWay. The value is shown with as many decimals as <see cref="SmallChange"/> has, up to four.
/// </summary>
public sealed class NumberBox : Control
{
	private static readonly SolidColorBrush SpinStroke = Brush.Sealed(HdrColor.FromRgb(170, 176, 190));

	public static readonly DependencyProperty<NumberBox, double> ValueProperty =
		DependencyProperty.Register(nameof(Value),
			new PropertyMetadata<NumberBox, double> { DefaultValue = double.NaN, PropertyChanged = static (n, _, _) => n.OnValueChanged() });

	private readonly TextBox _text = new();
	private readonly StackPanel _spin = new() { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4f, 0f, 0f, 0f) };
	private readonly ContentPresenter _header = new() { Margin = new Thickness(0f, 0f, 0f, 6f), Visibility = Visibility.Collapsed };
	private readonly StackPanel _column = new() { Orientation = Orientation.Vertical };
	private bool _showing;

	public NumberBox()
	{
		SetBuiltInValue(IsTabStopProperty, false);
		var row = new Grid();
		row.ColumnDefinitions.Add(GridLength.Star);
		row.ColumnDefinitions.Add(GridLength.Auto);
		Grid.SetColumn(_spin, 1);
		row.Children.Add(_text);
		row.Children.Add(_spin);
		_spin.Children.Add(Spin(Marks.ChevronUp(8f, SpinStroke), 1, "Increase"));
		_spin.Children.Add(Spin(Marks.ChevronDown(8f, SpinStroke), -1, "Decrease"));
		_column.Children.Add(_header);
		_column.Children.Add(row);
		_text.AddHandler(PreviewKeyDownEvent, OnPreviewKey);
		_text.AddHandler(LostFocusEvent, (_, _) => Commit());
		AddHandler(PointerWheelChangedEvent, OnWheel);
		SetBuiltInValue(TemplateProperty, new ControlTemplate { Factory = static control => ((NumberBox)control)._column });
		ShowValue();
		ShowSpin();
	}

	/// <summary>The number, or NaN for none.</summary>
	public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

	/// <summary>The least the value may be.</summary>
	public double Minimum { get; set { field = value; Value = Clamp(Value); } } = double.MinValue;

	/// <summary>The most the value may be.</summary>
	public double Maximum { get; set { field = value; Value = Clamp(Value); } } = double.MaxValue;

	/// <summary>The step of Up, Down and a wheel notch. 1 by default.</summary>
	public double SmallChange { get; set { field = value; ShowValue(); } } = 1d;

	/// <summary>The step of Page Up and Page Down. 10 by default.</summary>
	public double LargeChange { get; set; } = 10d;

	/// <summary>Where the up and down buttons show. Hidden by default.</summary>
	public NumberBoxSpinButtonPlacementMode SpinButtonPlacementMode { get; set { field = value; ShowSpin(); } }

	/// <summary>Shown above the box, or nothing where null.</summary>
	public object? Header
	{
		get;
		set
		{
			field = value;
			_header.Content = value;
			_header.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
		}
	}

	/// <summary>Raised as <see cref="Value"/> changes, so it binds TwoWay.</summary>
	public event EventHandler? ValueChanged;

	/// <summary>Steps the value by <paramref name="steps"/> of <see cref="SmallChange"/>; from no value, starts at 0
	/// clamped.</summary>
	public void Step(double steps) => Value = Clamp((double.IsNaN(Value) ? 0d : Value) + steps * SmallChange);

	private double Clamp(double value) => double.IsNaN(value) ? value : Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));

	private void OnValueChanged()
	{
		var clamped = Clamp(Value);
		if (!clamped.Equals(Value))
		{
			Value = clamped;
			return;
		}
		ShowValue();
		ValueChanged?.Invoke(this, EventArgs.Empty);
	}

	// What is typed taken as the value, or the value shown again
	private void Commit()
	{
		var text = _text.Text.Trim();
		if (text.Length == 0)
			Value = double.NaN;
		else if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed)
			|| double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out typed))
			Value = Clamp(typed);
		ShowValue();
	}

	private void ShowValue()
	{
		if (_showing)
			return;
		_showing = true;
		try
		{
			var decimals = Math.Min(4, Decimals(SmallChange));
			_text.Text = double.IsNaN(Value) ? "" : Value.ToString("F" + decimals, CultureInfo.CurrentCulture);
		}
		finally
		{
			_showing = false;
		}
	}

	private void ShowSpin() =>
		_spin.Visibility = SpinButtonPlacementMode == NumberBoxSpinButtonPlacementMode.Hidden ? Visibility.Collapsed : Visibility.Visible;

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

	private Button Spin(UIElement mark, int direction, string tip)
	{
		var button = new Button { Content = mark, Padding = new Thickness(6f, 1f, 6f, 1f), IsTabStop = false };
		ToolTipService.SetToolTip(button, tip);
		button.Click += (_, _) =>
		{
			Commit();
			Step(direction);
		};
		return button;
	}

	private void OnPreviewKey(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key)
			return;
		switch (key.Key)
		{
			case VirtualKey.Enter: Commit(); break;
			case VirtualKey.Up: Commit(); Step(1); break;
			case VirtualKey.Down: Commit(); Step(-1); break;
			case VirtualKey.PageUp: Commit(); Value = Clamp((double.IsNaN(Value) ? 0d : Value) + LargeChange); break;
			case VirtualKey.PageDown: Commit(); Value = Clamp((double.IsNaN(Value) ? 0d : Value) - LargeChange); break;
			default: return;
		}
		e.Handled = true;
	}

	private void OnWheel(object sender, RoutedEventArgs e)
	{
		if (e is not PointerRoutedEventArgs { Wheel.Y: var notch and not 0f })
			return;
		Commit();
		Step(MathF.Sign(notch));
		e.Handled = true;
	}
}
