using System.ComponentModel;
using System.Windows.Input;

using SUIsei.Controls;
using SUIsei.RoutedEvents;

namespace SUIsei.Gallery.Pages;

/// <summary>
/// Code-behind for <c>ControlsPage.xaml</c>: the bind sources (<see cref="Name"/>, <see cref="Greeting"/>,
/// <see cref="GreetCommand"/> and the nested <see cref="Render"/> settings) and the buttons' handlers.
/// </summary>
internal sealed partial class ControlsPage : IGalleryView, INotifyPropertyChanged
{
	private int _clicks;

	public ControlsPage()
	{
		GreetCommand = new RelayCommand(_ => Increment());
		InitializeComponent();
	}

	UIElement IGalleryView.Root => Root;

	/// <summary>x:Bind source bound to the "Greet" button's <see cref="ButtonBase.Command"/>.</summary>
	public ICommand GreetCommand { get; }

	/// <summary>Nested-bind root: <c>{x:Bind Render.Bloom.Intensity}</c> is a three-segment path, so the slider and its
	/// label exercise deep x:Bind (read, TwoWay write-back, and change tracking through each level).</summary>
	public RenderSettings Render { get; } = new();

	/// <summary>Two-way bound to the <see cref="TextBox"/>; drives <see cref="Greeting"/>.</summary>
	public string Name
	{
		get;
		set
		{
			if (field == value)
				return;
			field = value;
			OnPropertyChanged(nameof(Name));
			OnPropertyChanged(nameof(Greeting));
		}
	} = "world";

	/// <summary>One-way bound echo of <see cref="Name"/>.</summary>
	public string Greeting => $"Hello, {Name}!";

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnClick(object sender, RoutedEventArgs e) => Increment();

	private void OnReset(object sender, RoutedEventArgs e)
	{
		_clicks = 0;
		ClickCounter.Text = "Clicks: 0";
	}

	private void OnShowMenu(object sender, RoutedEventArgs e)
	{
		var flyout = new MenuFlyout();
		var increment = new MenuFlyoutItem { Text = "Increment" };
		increment.Click += (_, _) => Increment();
		var reset = new MenuFlyoutItem { Text = "Reset" };
		reset.Click += OnReset;
		flyout.Items.Add(increment);
		flyout.Items.Add(reset);
		flyout.ShowAt((UIElement)sender);
	}

	private void Increment()
	{
		_clicks++;
		ClickCounter.Text = $"Clicks: {_clicks}";
	}

	private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	/// <summary>The middle level of the nested-bind path <c>Render.Bloom.Intensity</c>: a plain holder of the bloom
	/// settings.</summary>
	internal sealed class RenderSettings
	{
		public BloomSettings Bloom { get; } = new();
	}

	/// <summary>The leaf level: a bloom setting a slider drives (TwoWay) and a label echoes (OneWay). The setter raises
	/// both the value and the formatted label so each bound target refreshes.</summary>
	internal sealed class BloomSettings : INotifyPropertyChanged
	{
		public double Intensity
		{
			get;
			set
			{
				if (field.Equals(value))
					return;
				field = value;
				OnChanged(nameof(Intensity));
				OnChanged(nameof(IntensityLabel));
			}
		} = 0.6;

		/// <summary>A formatted echo of <see cref="Intensity"/> for a OneWay text binding.</summary>
		public string IntensityLabel => $"Bloom intensity: {Intensity:0.00}";

		public event PropertyChangedEventHandler? PropertyChanged;

		private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
