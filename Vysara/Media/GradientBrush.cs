using Vysara.Composition;

namespace Vysara.Media;

/// <summary>
/// The stops and modes a gradient brush shares, as WinUI's <c>GradientBrush</c>; <see cref="LinearGradientBrush"/>
/// places them along a line. The stops are the brush's XAML content:
/// <code>&lt;LinearGradientBrush&gt;&lt;GradientStop Color="Red" Offset="0"/&gt;…&lt;/LinearGradientBrush&gt;</code>
/// <para>
/// Colours interpolate as premultiplied values. <see cref="ColorInterpolationMode.ScRgbLinearInterpolation"/> is drawn
/// exactly, colours above paper white included, by splitting what is painted where the stops fall: there is no ramp
/// texture to clamp or quantize them. The default, <see cref="ColorInterpolationMode.SRgbLinearInterpolation"/>, cuts each
/// stop interval into eight pieces, exact at their ends and within the error <see cref="CompositionGradientBrush.SrgbSubBands"/>
/// documents between them, and a stop above 1 in it throws when the brush is drawn.
/// </para>
/// </summary>
[ContentProperty(Name = nameof(GradientStops))]
public abstract class GradientBrush : Brush, ISubPropertyListener
{
	public static readonly DependencyProperty<GradientBrush, BrushMappingMode> MappingModeProperty =
		DependencyProperty.Register(nameof(MappingMode),
			new PropertyMetadata<GradientBrush, BrushMappingMode>
			{
				DefaultValue = BrushMappingMode.RelativeToBoundingBox,
				PropertyChanged = static (b, _, value) =>
				{
					b.Gradient.MappingMode = value == BrushMappingMode.Absolute ? CompositionMappingMode.Absolute : CompositionMappingMode.Relative;
					b.Changed();
				},
			});

	public static readonly DependencyProperty<GradientBrush, GradientSpreadMethod> SpreadMethodProperty =
		DependencyProperty.Register(nameof(SpreadMethod),
			new PropertyMetadata<GradientBrush, GradientSpreadMethod>
			{
				PropertyChanged = static (b, _, value) =>
				{
					b.Gradient.ExtendMode = value switch
					{
						GradientSpreadMethod.Reflect => CompositionGradientExtendMode.Mirror,
						GradientSpreadMethod.Repeat => CompositionGradientExtendMode.Wrap,
						_ => CompositionGradientExtendMode.Clamp,
					};
					b.Changed();
				},
			});

	public static readonly DependencyProperty<GradientBrush, ColorInterpolationMode> ColorInterpolationModeProperty =
		DependencyProperty.Register(nameof(ColorInterpolationMode),
			new PropertyMetadata<GradientBrush, ColorInterpolationMode>
			{
				PropertyChanged = static (b, _, value) =>
				{
					b.Gradient.InterpolationSpace = value == ColorInterpolationMode.ScRgbLinearInterpolation
						? CompositionColorSpace.RgbLinear
						: CompositionColorSpace.Rgb;
					b.Changed();
				},
			});

	// The stops copied for the paint, retained so a stop change allocates nothing once the brush has held as many.
	private CompositionColorGradientStop[] _stopBuffer = [];

	private protected GradientBrush(CompositionGradientBrush paint) : base(paint) => GradientStops = new GradientStopCollection(this);

	/// <summary>The stops, in any order.</summary>
	public GradientStopCollection GradientStops { get; }

	/// <summary>What <c>StartPoint</c> and <c>EndPoint</c> are measured in. Defaults to fractions of the area painted.</summary>
	public BrushMappingMode MappingMode { get => GetValue(MappingModeProperty); set => SetValue(MappingModeProperty, value); }

	/// <summary>What is painted before the gradient's start and past its end. Defaults to padding with the end colours.</summary>
	public GradientSpreadMethod SpreadMethod { get => GetValue(SpreadMethodProperty); set => SetValue(SpreadMethodProperty, value); }

	/// <summary>The space colours interpolate in. Defaults to the authored sRGB values, as in WinUI.</summary>
	public ColorInterpolationMode ColorInterpolationMode
	{
		get => GetValue(ColorInterpolationModeProperty);
		set => SetValue(ColorInterpolationModeProperty, value);
	}

	private protected CompositionGradientBrush Gradient => (CompositionGradientBrush)CompositionBrush;

	/// <summary>Copy the stops into the paint, telling the brush's users if that changed it.</summary>
	internal void SyncStops()
	{
		var stops = GradientStops.Items;
		if (_stopBuffer.Length < stops.Count)
			_stopBuffer = new CompositionColorGradientStop[stops.Count];
		for (var i = 0; i < stops.Count; i++)
			_stopBuffer[i] = new CompositionColorGradientStop(stops[i].Offset, stops[i].Color);
		var version = Gradient.Version;
		Gradient.SetColorStops(_stopBuffer.AsSpan(0, stops.Count));
		if (Gradient.Version != version)
			Changed();
	}

	void ISubPropertyListener.OnSubPropertyChanged(object source) => SyncStops();
}
