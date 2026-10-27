namespace SUIsei.Media;

/// <summary>Paints one colour, as WinUI's <c>SolidColorBrush</c>. In XAML a colour string (<c>Background="#22252E"</c>,
/// <c>Background="CornflowerBlue"</c>) makes one.</summary>
public sealed class SolidColorBrush : Brush
{
	/// <summary>The colour; animatable with a <c>ColorAnimation</c>, as
	/// <c>(Border.Background).(SolidColorBrush.Color)</c>.</summary>
	public static readonly DependencyProperty<SolidColorBrush, HdrColor> ColorProperty =
		DependencyProperty.Register(nameof(Color),
			new PropertyMetadata<SolidColorBrush, HdrColor>
			{
				DefaultValue = HdrColor.Transparent,
				PropertyChanged = static (b, _, value) =>
				{
					b.Paint.Color = value;
					b.Changed();
				},
			});

	/// <summary>A transparent brush.</summary>
	public SolidColorBrush() : base(new CompositionColorBrush())
	{
	}

	/// <summary>A brush of <paramref name="color"/>.</summary>
	public SolidColorBrush(HdrColor color) : this() => Color = color;

	/// <summary>The colour the brush paints.</summary>
	public HdrColor Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

	private CompositionColorBrush Paint => (CompositionColorBrush)CompositionBrush;

	public override string ToString() => $"SolidColorBrush {Color}";
}
