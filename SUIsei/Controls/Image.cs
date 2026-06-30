using SUIsei.Drawing;

namespace SUIsei.Controls;

/// <summary>
/// Draws an <see cref="ImageSource"/>, scaled into its layout bounds per <see cref="Stretch"/>.
/// Measures to the source's natural pixel size (in DIPs) so an unconstrained image takes its intrinsic size; an
/// explicit <c>Width</c>/<c>Height</c> on the element overrides that as usual.
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

	private protected override VisualContent? BuildContent() =>
		Source is { } source ? new ImageContent(source, ComputeDest(source, LayoutRect.Width, LayoutRect.Height, Stretch)) : null;

	// The destination rect (element-local) the source is drawn into. Fill stretches to the bounds; None uses the
	// natural size; Uniform fits preserving aspect (letterbox); UniformToFill covers preserving aspect (overflow,
	// clipped when the element clips).
	private static Rect ComputeDest(ImageSource source, float width, float height, Stretch stretch)
	{
		float iw = source.Width, ih = source.Height;
		if (iw <= 0f || ih <= 0f || stretch == Stretch.Fill)
			return new Rect(0f, 0f, width, height);
		if (stretch == Stretch.None)
			return new Rect(0f, 0f, iw, ih);

		var scale = stretch == Stretch.UniformToFill ? MathF.Max(width / iw, height / ih) : MathF.Min(width / iw, height / ih);
		var dw = iw * scale;
		var dh = ih * scale;
		return new Rect((width - dw) / 2f, (height - dh) / 2f, dw, dh);
	}
}
