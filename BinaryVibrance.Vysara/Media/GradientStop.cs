namespace BinaryVibrance.Vysara.Media;

/// <summary>
/// One colour of a <see cref="GradientBrush"/>, at <see cref="Offset"/> along it, as WinUI's <c>GradientStop</c>. Both
/// are animatable; a change reaches every brush holding the stop, and from there every element showing the brush, with no
/// layout and no allocation. Name a stop with <c>x:Name</c> to target it from a storyboard.
/// </summary>
public sealed class GradientStop : DependencyObject
{
	/// <summary>The stop's colour. Above 1 (above paper white) only in a gradient that interpolates in linear light.</summary>
	public static readonly DependencyProperty<GradientStop, HdrColor> ColorProperty =
		DependencyProperty.Register(nameof(Color),
			new PropertyMetadata<GradientStop, HdrColor> { PropertyChanged = static (s, _, _) => s._listeners.Notify(s) });

	/// <summary>Where the stop lies along the gradient: 0 at its start, 1 at its end.</summary>
	public static readonly DependencyProperty<GradientStop, float> OffsetProperty =
		DependencyProperty.Register(nameof(Offset),
			new PropertyMetadata<GradientStop, float> { PropertyChanged = static (s, _, _) => s._listeners.Notify(s) });

	private ListenerList _listeners;

	public HdrColor Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

	public float Offset { get => GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }

	internal void AddListener(ISubPropertyListener listener) => _listeners.Add(listener);

	internal void RemoveListener(ISubPropertyListener listener) => _listeners.Remove(listener);

	public override string ToString() => $"GradientStop {Offset} {Color}";
}
