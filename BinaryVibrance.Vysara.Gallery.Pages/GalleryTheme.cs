using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Gallery;

/// <summary>
/// The gallery's shared look: the page backdrop and the styles every page uses for its title, its sections and the cards
/// they sit on. A page takes them with <c>{x:Static}</c>, since a <c>{StaticResource}</c> resolves only within its own
/// XAML file.
/// </summary>
internal static class GalleryTheme
{
	/// <summary>The page backdrop. The renderer clears the framebuffer to it, so a resized region matches.</summary>
	public static readonly HdrColor PageBackground = HdrColor.FromRgb(24, 26, 32);

	private static readonly SolidColorBrush Ink = new(HdrColor.FromRgb(236, 239, 246));
	private static readonly SolidColorBrush Muted = new(HdrColor.FromRgb(150, 156, 170));
	private static readonly SolidColorBrush Accent = new(HdrColor.FromRgb(120, 170, 255));

	/// <summary>The page's title, at the top.</summary>
	public static readonly Style PageTitle = new()
	{
		Setters =
		{
			new Setter { Property = TextBlock.FontSizeProperty, Value = 30f },
			new Setter { Property = TextBlock.ForegroundProperty, Value = Ink },
		},
	};

	/// <summary>A line under the title saying what the page shows.</summary>
	public static readonly Style PageSummary = new()
	{
		Setters =
		{
			new Setter { Property = TextBlock.FontSizeProperty, Value = 14f },
			new Setter { Property = TextBlock.ForegroundProperty, Value = Muted },
			new Setter { Property = FrameworkElement.MarginProperty, Value = new Thickness(0f, 2f, 0f, 18f) },
		},
	};

	/// <summary>A section's heading, over its card.</summary>
	public static readonly Style SectionHeading = new()
	{
		Setters =
		{
			new Setter { Property = TextBlock.FontSizeProperty, Value = 13f },
			new Setter { Property = TextBlock.ForegroundProperty, Value = Accent },
			new Setter { Property = FrameworkElement.MarginProperty, Value = new Thickness(2f, 0f, 0f, 6f) },
		},
	};

	/// <summary>Text on a card.</summary>
	public static readonly Style Body = new()
	{
		Setters =
		{
			new Setter { Property = TextBlock.FontSizeProperty, Value = 15f },
			new Setter { Property = TextBlock.ForegroundProperty, Value = Ink },
		},
	};

	/// <summary>A note on a card, quieter than its body text.</summary>
	public static readonly Style Note = new()
	{
		Setters =
		{
			new Setter { Property = TextBlock.FontSizeProperty, Value = 13f },
			new Setter { Property = TextBlock.ForegroundProperty, Value = Muted },
		},
	};

	/// <summary>The card a section's demonstrations sit on.</summary>
	public static readonly Style Card = new()
	{
		Setters =
		{
			new Setter { Property = Border.BackgroundProperty, Value = new SolidColorBrush(HdrColor.FromRgb(34, 37, 46)) },
			new Setter { Property = Border.BorderBrushProperty, Value = new SolidColorBrush(HdrColor.FromRgb(58, 63, 78)) },
			new Setter { Property = Border.BorderThicknessProperty, Value = new Thickness(1f) },
			new Setter { Property = Border.CornerRadiusProperty, Value = new CornerRadius(10f) },
			new Setter { Property = Border.PaddingProperty, Value = new Thickness(16f) },
			new Setter { Property = FrameworkElement.MarginProperty, Value = new Thickness(0f, 0f, 0f, 18f) },
		},
	};
}
