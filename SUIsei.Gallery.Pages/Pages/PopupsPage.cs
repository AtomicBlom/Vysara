using System.ComponentModel;

using SUIsei.Controls;
using SUIsei.RoutedEvents;

namespace SUIsei.Gallery.Pages;

/// <summary>
/// Code-behind for <c>PopupsPage.xaml</c>: the list's tracks and the one chosen (<see cref="Track"/>, bound TwoWay), its
/// clicks and the note's scrolling said in words the page binds to, the rename flyout and the card's menu.
/// </summary>
internal sealed partial class PopupsPage : IGalleryView, INotifyPropertyChanged
{
	private int _clicks;

	public PopupsPage() => InitializeComponent();

	UIElement IGalleryView.Root => Root;

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>What the list shows: more than fit, so it scrolls.</summary>
	public string[] Tracks { get; } =
	[
		"Grand loop", "Harbour sprint", "Pine switchbacks", "Salt flats", "Quarry hairpins", "Old viaduct",
		"Coastal esses", "Night circuit", "Dam crest", "Tunnel run",
	];

	/// <summary>The track chosen, an index into <see cref="Tracks"/>; bound TwoWay by the list.</summary>
	public int Track
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			Changed(nameof(Track));
			Changed(nameof(TrackLabel));
		}
	} = 1;

	/// <summary>The track chosen, said in words.</summary>
	public string TrackLabel => Track >= 0 && Track < Tracks.Length ? $"Chosen: {Tracks[Track]}" : "Nothing chosen";

	/// <summary>How many times a row has been clicked, and which last.</summary>
	public string ClickLabel { get; private set { field = value; Changed(nameof(ClickLabel)); } } = "Not clicked yet";

	/// <summary>How far the note has been scrolled.</summary>
	public string ScrollLabel { get; private set { field = value; Changed(nameof(ScrollLabel)); } } = "Scrolled 0 px";

	/// <summary>The card's track, which the rename flyout renames.</summary>
	public string TrackName { get; private set { field = value; Changed(nameof(TrackName)); } } = "Grand loop";

	/// <summary>What the rename flyout's text box holds, bound TwoWay.</summary>
	public string NewName { get; set; } = "Grand loop";

	/// <summary>What the card's menu last did.</summary>
	public string MenuLabel { get; private set { field = value; Changed(nameof(MenuLabel)); } } = "Right-click here (or Shift F10) for its menu";

	private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

	private void OnTrackClick(object sender, ItemClickEventArgs e)
	{
		_clicks++;
		ClickLabel = $"Clicked {e.ClickedItem} ({_clicks} {(_clicks == 1 ? "click" : "clicks")})";
	}

	private void OnNotesScrolled(object? sender, ScrollViewerViewChangedEventArgs e) =>
		ScrollLabel = $"Scrolled {((ScrollViewer)sender!).VerticalOffset:0} px";

	private void OnRename(object sender, RoutedEventArgs e)
	{
		TrackName = NewName;
		RenameFlyout.Hide();
	}

	private void OnDuplicate(object sender, RoutedEventArgs e) => MenuLabel = $"Duplicated {TrackName}";

	private void OnReverse(object sender, RoutedEventArgs e) => MenuLabel = $"Reversed {TrackName}";

	private void OnForget(object sender, RoutedEventArgs e) => MenuLabel = $"Forgot {TrackName}";
}
