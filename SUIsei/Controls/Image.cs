using SUIsei.Composition;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// Draws an <see cref="ImageSource"/>, scaled into its layout bounds per <see cref="Stretch"/> and cut at them.
/// Measures to the source's natural pixel size (in DIPs) so an unconstrained image takes its intrinsic size; an
/// explicit <c>Width</c>/<c>Height</c> on the element overrides that as usual. Its content is a rectangle filled by a
/// <see cref="CompositionSurfaceBrush"/>, centred, except that <see cref="Stretch.None"/> keeps the image at the top
/// left.
/// </summary>
public sealed class Image : FrameworkElement
{
	public ImageSource? Source
	{
		get;
		set { if (!ReferenceEquals(field, value)) { field = value; InvalidateMeasure(); } }
	}

	public Stretch Stretch
	{
		get;
		set { if (field != value) { field = value; InvalidateContent(); } }
	} = Stretch.Uniform;

	protected override Size MeasureOverride(Size available) =>
		Source is { } source ? new Size(source.Width, source.Height) : Size.Zero;

	protected override Size ArrangeOverride(Size finalSize) => finalSize;

	// The paint this element draws with, kept across updates so a re-arrange allocates nothing.
	private CompositionSurfaceBrush? _paint;

	private protected override VisualContent? UpdateContent()
	{
		if (Source is not { } source)
			return null;
		var paint = _paint ??= new CompositionSurfaceBrush();
		paint.Surface = source;
		paint.Stretch = Stretch.ToComposition();
		var ratio = Stretch == Stretch.None ? 0f : 0.5f;
		paint.HorizontalAlignmentRatio = ratio;
		paint.VerticalAlignmentRatio = ratio;
		return RetainedRect(paint);
	}
}
