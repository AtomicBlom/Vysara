using SUIsei.Gallery.Pages;

namespace SUIsei.Gallery;

/// <summary>The gallery's pages, in the order the shell lists them.</summary>
internal static class GalleryPages
{
	public static IReadOnlyList<GalleryPage> All { get; } =
	[
		new("text", "Text", static () => new TextPage()),
		new("controls", "Controls", static () => new ControlsPage()),
		new("layout", "Layout", static () => new LayoutPage()),
		new("images", "Images", static () => new ImagesPage()),
	];

	/// <summary>The page <paramref name="key"/> names, or an <see cref="ArgumentException"/> listing the keys there are.</summary>
	public static GalleryPage Find(string key) =>
		All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
		?? throw new ArgumentException($"No gallery page '{key}'. The pages are: {string.Join(", ", All.Select(static p => p.Key))}.");
}
