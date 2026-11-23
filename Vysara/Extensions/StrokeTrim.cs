using Vysara.Shapes;

namespace Vysara.Extensions;

/// <summary>
/// The part of a shape's stroke that is drawn, set in XAML as <c>vysara:StrokeTrim.End="0.5"</c>: WinUI's
/// <c>CompositionGeometry.TrimStart</c>, <c>TrimEnd</c> and <c>TrimOffset</c> lifted into markup, for draw-on reveals and
/// segments that run round an outline. The values are fractions of the whole outline's length, all its figures in order:
/// the stroke runs from <c>Start</c> to <c>End</c>, both moved along by <c>Offset</c> and wrapping past the end back to
/// the start. Start and end are clamped to 0..1 and swapped when start is the larger; equal, they draw nothing.
/// <para>
/// The trim applies where the shape is drawn: animating it, through a path such as <c>(vysara:StrokeTrim.End)</c>,
/// costs no layout and no tessellation, and allocates nothing. A trimmed stroke takes its start cap where it starts and
/// its end cap where it ends. The fill is not trimmed.
/// </para>
/// </summary>
public sealed class StrokeTrim : DependencyObject
{
	private StrokeTrim()
	{
	}

	/// <summary>Where the drawn part of the stroke starts, as a fraction of the outline's length. 0 by default.</summary>
	public static readonly DependencyProperty<Shape, float> StartProperty = Register("Start", 0f);

	/// <summary>Where the drawn part of the stroke ends, as a fraction of the outline's length. 1 by default.</summary>
	public static readonly DependencyProperty<Shape, float> EndProperty = Register("End", 1f);

	/// <summary>How far both ends are moved along the outline, as a fraction of its length, wrapping past its end. 0 by
	/// default.</summary>
	public static readonly DependencyProperty<Shape, float> OffsetProperty = Register("Offset", 0f);

	public static void SetStart(Shape shape, float value) => shape.SetValue(StartProperty, value);
	public static float GetStart(Shape shape) => shape.GetValue(StartProperty);

	public static void SetEnd(Shape shape, float value) => shape.SetValue(EndProperty, value);
	public static float GetEnd(Shape shape) => shape.GetValue(EndProperty);

	public static void SetOffset(Shape shape, float value) => shape.SetValue(OffsetProperty, value);
	public static float GetOffset(Shape shape) => shape.GetValue(OffsetProperty);

	private static DependencyProperty<Shape, float> Register(string name, float defaultValue) =>
		DependencyProperty.RegisterAttached(name,
			new PropertyMetadata<Shape, float> { DefaultValue = defaultValue, PropertyChanged = static (shape, _, _) => shape.ApplyTrim() });
}
