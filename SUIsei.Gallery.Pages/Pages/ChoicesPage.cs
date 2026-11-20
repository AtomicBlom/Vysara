using System.ComponentModel;

using SUIsei.RoutedEvents;

namespace SUIsei.Gallery.Pages;

/// <summary>
/// Code-behind for <c>ChoicesPage.xaml</c>: the choices its controls bind (<see cref="Weather"/>, shared by a ComboBox and
/// RadioButtons; <see cref="Laps"/>; <see cref="Ghosts"/>), the lines echoing them, and reopening the InfoBars.
/// </summary>
internal sealed partial class ChoicesPage : IGalleryView, INotifyPropertyChanged
{
	public ChoicesPage() => InitializeComponent();

	UIElement IGalleryView.Root => Root;

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>What the ComboBox and the RadioButtons choose from.</summary>
	public string[] Weathers { get; } = ["Clear", "Cloudy", "Rain", "Fog"];

	/// <summary>The weather chosen, as an index into <see cref="Weathers"/>; bound TwoWay by both controls.</summary>
	public int Weather
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			OnPropertyChanged(nameof(Weather));
			OnPropertyChanged(nameof(WeatherLabel));
		}
	} = 2;

	/// <summary>The weather chosen, said in words.</summary>
	public string WeatherLabel => Weather >= 0 && Weather < Weathers.Length ? $"The weather is {Weathers[Weather]}." : "No weather chosen.";

	/// <summary>How many laps, bound TwoWay by the NumberBox.</summary>
	public double Laps
	{
		get;
		set
		{
			if (field.Equals(value))
				return;
			field = value;
			OnPropertyChanged(nameof(Laps));
			OnPropertyChanged(nameof(RaceLabel));
		}
	} = 3;

	/// <summary>Whether ghosts are shown, bound TwoWay by the ToggleSwitch.</summary>
	public bool Ghosts
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			OnPropertyChanged(nameof(Ghosts));
			OnPropertyChanged(nameof(RaceLabel));
		}
	} = true;

	/// <summary>The race as chosen, said in words.</summary>
	public string RaceLabel => $"{Laps:0} {(Laps == 1 ? "lap" : "laps")}, ghosts {(Ghosts ? "shown" : "hidden")}.";

	private void OnReopen(object sender, RoutedEventArgs e)
	{
		Informational.IsOpen = true;
		Success.IsOpen = true;
		Warning.IsOpen = true;
		Error.IsOpen = true;
	}

	private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
