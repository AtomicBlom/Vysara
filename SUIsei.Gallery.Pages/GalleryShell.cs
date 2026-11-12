using SUIsei.Controls;
using SUIsei.RoutedEvents;

namespace SUIsei.Gallery;

/// <summary>
/// Code-behind for <c>GalleryShell.xaml</c>: lists <see cref="GalleryPages.All"/> down the left and in the View menu, and
/// shows the page picked. Each page is made the first time it is shown and kept, so it keeps its state (a slider's
/// value, a click count) when another is shown and it is shown again.
/// </summary>
internal sealed partial class GalleryShell : IGalleryView
{
	private readonly Dictionary<GalleryPage, IGalleryView> _made = [];
	private IGalleryView? _shown;

	/// <summary>The shell, showing <paramref name="page"/> (by its key), or the first page.</summary>
	public GalleryShell(string? page = null)
	{
		InitializeComponent();
		Sections.ItemsSource = GalleryPages.All;
		foreach (var each in GalleryPages.All)
		{
			var item = new MenuFlyoutItem { Text = each.Title };
			item.Click += (_, _) => Show(each.Key);
			ViewMenu.Items.Add(item);
		}
		Show(page ?? GalleryPages.All[0].Key);
	}

	UIElement IGalleryView.Root => Root;

	/// <summary>Invoked when the user picks Quit from the File menu.</summary>
	public Action? Quit { get; set; }

	/// <summary>The page shown.</summary>
	public GalleryPage Page { get; private set; } = GalleryPages.All[0];

	/// <summary>Show the page <paramref name="key"/> names; an unknown key throws, listing the keys there are.</summary>
	public void Show(string key)
	{
		var page = GalleryPages.Find(key);
		Sections.SelectIndex(IndexOf(page));
	}

	public void Animate(double seconds) => _shown?.Animate(seconds);

	private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Sections.SelectedItem is not GalleryPage page)
			return;
		if (!_made.TryGetValue(page, out var view))
			_made[page] = view = page.Create();
		Page = page;
		_shown = view;
		PageHost.Content = view.Root;
		PageHost.ScrollToVerticalOffset(0f);
	}

	private void OnQuit(object sender, RoutedEventArgs e) => Quit?.Invoke();

	private static int IndexOf(GalleryPage page)
	{
		for (var i = 0; i < GalleryPages.All.Count; i++)
		{
			if (ReferenceEquals(GalleryPages.All[i], page))
				return i;
		}
		return -1;
	}
}
