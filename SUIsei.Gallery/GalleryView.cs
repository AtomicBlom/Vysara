using System.ComponentModel;
using System.Windows.Input;

using SUIsei.Controls;
using SUIsei.Media.Imaging;
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
	public BitmapSource LogoSource { get; } = MakeLogo();

	/// <summary>x:Bind source for the live <see cref="Image"/>: a straight-alpha bitmap <see cref="Animate"/> rewrites
	/// every frame.</summary>
	public WriteableBitmap LiveBitmap { get; } = new(64, 40);

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

	/// <summary>
	/// Rewrite <see cref="LiveBitmap"/> for the time <paramref name="seconds"/>: rings rippling out of a disc whose rim
	/// fades out and whose troughs are half transparent, so its straight alpha blends over the card. The texels change in
	/// place and <see cref="WriteableBitmap.Invalidate"/> shows them, with no layout and no allocation.
	/// </summary>
	public void Animate(double seconds)
	{
		var bitmap = LiveBitmap;
		var pixels = bitmap.PixelBuffer.Span;
		var t = (float)seconds;
		for (var y = 0; y < bitmap.PixelHeight; y++)
		{
			for (var x = 0; x < bitmap.PixelWidth; x++)
			{
				var r = MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 19.5f) * (y - 19.5f));
				var ring = 0.5f + 0.5f * MathF.Cos(r * 0.6f - t * 5f);
				var rim = Math.Clamp((19.5f - r) / 5f, 0f, 1f);
				var o = (y * bitmap.PixelWidth + x) * 4;
				pixels[o] = (byte)(40f + 120f * ring);
				pixels[o + 1] = (byte)(200f - 110f * ring);
				pixels[o + 2] = (byte)(190f + 65f * ring);
				pixels[o + 3] = (byte)(255f * rim * (0.5f + 0.5f * ring));
			}
		}
		bitmap.Invalidate();
	}

	// A small RGBA gradient so the Image control has something to show without shipping an asset file.
	private static BitmapSource MakeLogo()
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
		return BitmapSource.FromRgba8(width, height, pixels);
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
