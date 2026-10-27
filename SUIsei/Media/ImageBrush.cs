namespace SUIsei.Media;

/// <summary>
/// Paints an image, as WinUI's <c>ImageBrush</c>: <see cref="ImageSource"/> scaled into the area painted by
/// <see cref="TileBrush.Stretch"/> and placed by <see cref="TileBrush.AlignmentX"/> and <see cref="TileBrush.AlignmentY"/>,
/// cut at the area's edges. It paints a <c>Panel</c>'s or <c>Border</c>'s background, rounded or not, a <c>Border</c>'s
/// border, and a shape's fill or stroke; text is not supported yet, and throws <see cref="NotSupportedException"/>
/// when drawn.
/// </summary>
public sealed class ImageBrush : TileBrush
{
	public static readonly DependencyProperty<ImageBrush, ImageSource?> ImageSourceProperty =
		DependencyProperty.Register(nameof(ImageSource),
			new PropertyMetadata<ImageBrush, ImageSource?>
			{
				PropertyChanged = static (b, _, value) =>
				{
					b.Surface.Surface = value;
					b.Changed();
				},
			});

	/// <summary>The image; null paints nothing.</summary>
	public ImageSource? ImageSource { get => GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
}
