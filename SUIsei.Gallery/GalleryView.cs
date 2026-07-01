using System.ComponentModel;
using System.Windows.Input;

using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.RoutedEvents;

namespace SUIsei.Gallery;

/// <summary>
/// Code-behind for <c>GalleryView.xaml</c>. The generator emits the partial half (the typed control tree, the
/// <c>x:Name</c> fields, event wiring, and the <c>{x:Bind}</c> glue); this half supplies the bind sources
/// (<see cref="Name"/>/<see cref="Greeting"/>/<see cref="LogoSource"/>/<see cref="GreetCommand"/>) and the handlers.
/// </summary>
internal sealed partial class GalleryView : INotifyPropertyChanged
{
	/// <summary>The page backdrop — the window host clears the framebuffer to this so resized regions match.</summary>
	public static readonly HdrColor PageBackground = HdrColor.FromRgb(24, 26, 32);

	private int _clicks;

	public GalleryView()
	{
		GreetCommand = new RelayCommand(_ => Increment());
		InitializeComponent();
	}

	/// <summary>Invoked when the user picks Quit from the menu.</summary>
	public Action? Quit { get; set; }

	/// <summary>x:Bind source for the <see cref="Image"/>: a procedurally-generated logo.</summary>
	public ImageSource LogoSource { get; } = MakeLogo();

	/// <summary>x:Bind source bound to the "Greet" button's <see cref="ButtonBase.Command"/>.</summary>
	public ICommand GreetCommand { get; }

	/// <summary>Nested-bind demo root: <c>{x:Bind Render.Bloom.Intensity}</c> is a three-segment path, so the slider
	/// and its label exercise deep x:Bind (read, TwoWay write-back, and INPC tracking through each level).</summary>
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

	private void OnMenuIncrement(object sender, RoutedEventArgs e) => Increment();

	private void OnMenuReset(object sender, RoutedEventArgs e) => OnReset(sender, e);

	private void OnMenuQuit(object sender, RoutedEventArgs e) => Quit?.Invoke();

	private void OnShowMenu(object sender, RoutedEventArgs e)
	{
		var flyout = new MenuFlyout();
		var increment = new MenuFlyoutItem { Text = "Increment" };
		increment.Click += (_, _) => Increment();
		var quit = new MenuFlyoutItem { Text = "Quit" };
		quit.Click += (_, _) => Quit?.Invoke();
		flyout.Items.Add(increment);
		flyout.Items.Add(quit);
		flyout.ShowAt((UIElement)sender);
	}

	private void Increment()
	{
		_clicks++;
		ClickCounter.Text = $"Clicks: {_clicks}";
	}

	private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	// A small RGBA gradient so the Image control has something to show without shipping an asset file.
	private static ImageSource MakeLogo()
	{
		const int width = 64;
		const int height = 40;
		var pixels = new byte[width * height * 4];
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var o = (y * width + x) * 4;
				pixels[o] = (byte)(x * 255 / width);
				pixels[o + 1] = (byte)(y * 255 / height);
				pixels[o + 2] = 180;
				pixels[o + 3] = 255;
			}
		}
		return ImageSource.FromRgba8(width, height, pixels);
	}

	/// <summary>The middle level of the nested-bind demo path <c>Render.Bloom.Intensity</c> — a plain holder of the
	/// (INPC) bloom settings.</summary>
	internal sealed class RenderSettings
	{
		public BloomSettings Bloom { get; } = new();
	}

	/// <summary>The leaf level: an INPC bloom setting a slider drives (TwoWay) and a label echoes (OneWay). The
	/// setter raises both the value and the formatted label so each bound target refreshes.</summary>
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
