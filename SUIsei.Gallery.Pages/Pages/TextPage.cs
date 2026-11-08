namespace SUIsei.Gallery.Pages;

/// <summary>Code-behind for <c>TextPage.xaml</c>.</summary>
internal sealed partial class TextPage : IGalleryView
{
	public TextPage() => InitializeComponent();

	UIElement IGalleryView.Root => Root;
}
