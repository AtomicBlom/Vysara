using System.ComponentModel;

using SUIsei.Controls;
using SUIsei.RoutedEvents;

namespace SUIsei.Gallery.Pages;

/// <summary>
/// Code-behind for <c>PopupsPage.xaml</c>: the list's tracks and the one chosen (<see cref="Track"/>, bound TwoWay), its
/// clicks and scrolling echoed, the rename flyout and the card's menu.
/// </summary>
internal sealed partial class PopupsPage : IGalleryView, INotifyPropertyChanged
{
	private int _clicks;

	public PopupsPage()
	{
		InitializeComponent();
		ListScroller.ViewChanged += (_, _) => Scrolled.Text = $"Scrolled {ListScroller.VerticalOffset:0} px";
	}

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
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Track)));
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackLabel)));
		}
	} = 1;

	/// <summary>The track chosen, said in words.</summary>
	public string TrackLabel => Track >= 0 && Track < Tracks.Length ? $"Chosen: {Tracks[Track]}" : "Nothing chosen";

	private void OnTrackClick(object sender, ItemClickEventArgs e)
	{
		_clicks++;
		Clicks.Text = $"Clicked {e.ClickedItem} ({_clicks} {(_clicks == 1 ? "click" : "clicks")})";
	}

	private void OnRename(object sender, RoutedEventArgs e)
	{
		TrackName.Text = NewName.Text;
		RenameFlyout.Hide();
	}

	private void OnDuplicate(object sender, RoutedEventArgs e) => Menued.Text = $"Duplicated {TrackName.Text}";

	private void OnReverse(object sender, RoutedEventArgs e) => Menued.Text = $"Reversed {TrackName.Text}";

	private void OnForget(object sender, RoutedEventArgs e) => Menued.Text = $"Forgot {TrackName.Text}";
}
