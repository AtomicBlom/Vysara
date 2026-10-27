using SUIsei.Shapes;

namespace SUIsei.Extensions;

/// <summary>How a shape's <c>Stroke</c> gradient is laid over the stroke, set by <see cref="StrokeBrushMapping"/>.</summary>
public enum StrokeBrushMappingMode
{
	/// <summary>As on any element: the gradient lies in space, along its own start and end points over the shape's
	/// bounds.</summary>
	Spatial,

	/// <summary>Along the stroke: a gradient's offsets run along the outline's untrimmed length, 0 at its start and 1 at
	/// its end, all its figures in order; its start and end points are not used.</summary>
	AlongLength,
}

/// <summary>
/// How a shape's stroke gradient is mapped, set in XAML as <c>suisei:StrokeBrushMapping.Mode="AlongLength"</c>: with
/// <see cref="StrokeBrushMappingMode.AlongLength"/> the gradient's offsets run along the stroke's untrimmed length, for
/// a fade along a trail or a line coloured by progress. WinUI has no such mapping; it is set on the shape rather than
/// the brush, so a brush shared by several elements never changes meaning with who shows it.
/// <para>
/// It is exact: the stroke is cut where its colour stops being linear along it (at each stop), so a stop's colour lands
/// exactly at its offset along the length. It suits a few stops; colouring each sample of a long line differently is a
/// different feature, not provided. A solid or image stroke draws as usual.
/// </para>
/// </summary>
public sealed class StrokeBrushMapping : DependencyObject
{
	private StrokeBrushMapping()
	{
	}

	/// <summary>How the stroke's gradient is mapped. <see cref="StrokeBrushMappingMode.Spatial"/> by default.</summary>
	public static readonly DependencyProperty<Shape, StrokeBrushMappingMode> ModeProperty =
		DependencyProperty.RegisterAttached("Mode",
			new PropertyMetadata<Shape, StrokeBrushMappingMode> { PropertyChanged = static (shape, _, _) => shape.OnStrokeMappingChanged() });

	public static void SetMode(Shape shape, StrokeBrushMappingMode value) => shape.SetValue(ModeProperty, value);
	public static StrokeBrushMappingMode GetMode(Shape shape) => shape.GetValue(ModeProperty);
}
