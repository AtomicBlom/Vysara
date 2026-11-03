namespace SUIsei.Gallery.Pages;

/// <summary>Code-behind for <c>LayoutPage.xaml</c>.</summary>
internal sealed partial class LayoutPage : IGalleryView
{
	public LayoutPage() => InitializeComponent();

	UIElement IGalleryView.Root => Root;
}
