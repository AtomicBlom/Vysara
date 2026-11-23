using System.Numerics;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.RoseTap;

/// <summary>
/// The overlay's elements on one view: a full-surface diagnostics overlay holding the capture layer that collects a pick,
/// the marks drawn over the app, and the toolbar. Built from Vysara's own elements and added with
/// <see cref="Diagnostics.AddOverlay"/>, so it draws into the surface the game already draws and is hit-tested first.
/// <para>
/// Click-through is structural, as in the XAML taps: a panel with no background draws nothing and so is never hit, so
/// the root and the marks let every click through to the game, and only the toolbar, which has a background, takes
/// input. Arming a pointer mode shows the capture layer, a transparent fill that is hit everywhere beneath the toolbar.
/// </para>
/// <para>
/// Marks are placed in the view's DIPs: <see cref="Diagnostics.Bounds"/> gives surface pixels, divided by the view's
/// layout scale.
/// </para>
/// </summary>
internal sealed class TapOverlayView
{
	// The view's own brushes, made once with it: a brush notifies the elements that paint with it, so one shared across
	// views or sessions would hold on to elements of an overlay that has gone.
	private readonly SolidColorBrush _panel = new(HdrColor.FromRgb(24, 26, 34, 230));
	private readonly SolidColorBrush _idle = new(HdrColor.FromRgb(52, 56, 70));
	private readonly SolidColorBrush _accent = new(HdrColor.FromRgb(14, 124, 150));
	private readonly SolidColorBrush _disabled = new(HdrColor.FromRgb(40, 42, 50));
	private readonly SolidColorBrush _ink = new(HdrColor.FromRgb(235, 238, 245));
	private readonly SolidColorBrush _badgeInk = new(HdrColor.FromRgb(10, 10, 14));
	private readonly SolidColorBrush _hover = new(HdrColor.FromRgb(80, 170, 255));
	private readonly SolidColorBrush _pick = new(HdrColor.FromRgb(255, 170, 40));
	private readonly SolidColorBrush _change = new(HdrColor.FromRgb(120, 220, 120));
	private readonly SolidColorBrush _ruler = new(HdrColor.FromRgb(255, 80, 160));

	private readonly Dictionary<string, Border> _buttons = [];
	private readonly List<(Border Box, Border Tag)> _changeMarks = [];
	private readonly WeakReference<UIHost> _host;

	public TapOverlayView(UIHost host, UIView view, TapOverlay owner)
	{
		_host = new WeakReference<UIHost>(host);
		View = view;

		// Transparent rather than no brush: a fill is what makes the layer hit everywhere it covers.
		Capture = new Border { Background = new SolidColorBrush(HdrColor.Transparent), Visibility = Visibility.Collapsed };
		Capture.AddHandler(UIElement.PointerMovedEvent, (_, e) => owner.OnHover(this, ((PointerRoutedEventArgs)e).Pointer.Position));
		Capture.AddHandler(UIElement.PointerPressedEvent, (_, e) =>
		{
			e.Handled = true; // a pick never reaches the game
			owner.OnPick(this, ((PointerRoutedEventArgs)e).Pointer.Position);
		});

		Marks = new Canvas { IsHitTestVisible = false };
		HoverBox = Outline(_hover, 1f);
		HoverBadge = Badge(_hover);
		PickBox = Outline(_pick, 2f);
		PickBadge = Badge(_pick);
		AnchorBox = Outline(_ruler, 1f);
		for (var i = 0; i < Leaders.Length; i++)
		{
			Leaders[i] = Line();
			LeaderLabels[i] = Badge(_ruler);
		}
		Lens = new Border { BorderBrush = _ink, BorderThickness = new Thickness(1f), Child = new Canvas(), Visibility = Visibility.Collapsed };
		Marks.Children.Add(Lens);

		var buttons = new StackPanel { Orientation = Orientation.Horizontal };
		foreach (var (name, action) in owner.ToolbarActions())
			buttons.Children.Add(_buttons[name] = Button(name, action));
		RulersText = Label("", 11f);
		RulersText.Visibility = Visibility.Collapsed;

		ZoomRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
		foreach (var (name, action) in owner.Zoom.Actions())
			ZoomRow.Children.Add(_buttons[name] = Button(name, () => action(owner)));
		Swatch = new Border
		{
			Width = 12f,
			Height = 12f,
			Margin = new Thickness(4f, 0f),
			BorderBrush = _ink,
			BorderThickness = new Thickness(1f),
			Background = SwatchBrush,
		};
		ZoomRow.Children.Add(Swatch);
		ZoomText = Label("", 11f);
		ZoomRow.Children.Add(ZoomText);

		var rows = new StackPanel();
		rows.Children.Add(buttons);
		rows.Children.Add(RulersText);
		rows.Children.Add(ZoomRow);
		Toolbar = new Border
		{
			Background = _panel,
			Padding = new Thickness(4f),
			Margin = new Thickness(8f),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Child = rows,
		};

		Root = new Grid();
		Root.Children.Add(Capture);
		Root.Children.Add(Marks);
		Root.Children.Add(Toolbar);
	}

	public UIView View { get; }

	/// <summary>The element added as the view's diagnostics overlay.</summary>
	public Grid Root { get; }

	/// <summary>The layer an armed pointer mode collects hovers and clicks on.</summary>
	public Border Capture { get; }

	/// <summary>Where every mark is drawn, never hit-tested.</summary>
	public Canvas Marks { get; }

	public Border Toolbar { get; }
	public Border HoverBox { get; }
	public Border HoverBadge { get; }
	public Border PickBox { get; }
	public Border PickBadge { get; }
	public Border AnchorBox { get; }

	/// <summary>The rulers' dimension lines and their numbers: up to two per axis.</summary>
	public Border[] Leaders { get; } = new Border[4];
	public Border[] LeaderLabels { get; } = new Border[4];
	/// <summary>The length each leader's label shows, so its text is rebuilt only when the length moves.</summary>
	public float[] LeaderLengths { get; } = [float.NaN, float.NaN, float.NaN, float.NaN];

	/// <summary>The toolbar's rulers row: the anchor's size, margin and padding, and a pinned measurement.</summary>
	public TextBlock RulersText { get; }

	/// <summary>The host the view belongs to, whose last draw lists the lens rasterizes; null once the game has let it go,
	/// which the overlay takes as the view being gone. Held weakly, so a tap never keeps a host alive.</summary>
	public UIHost? Host => _host.TryGetTarget(out var host) ? host : null;

	/// <summary>The toolbar's magnifier row: its modes and factor, the colour under the lens, and where the pixels came
	/// from.</summary>
	public StackPanel ZoomRow { get; }

	public TextBlock ZoomText { get; }

	/// <summary>The colour under the lens, as a filled square.</summary>
	public Border Swatch { get; }

	/// <summary>The swatch's fill, recoloured in place as the lens moves.</summary>
	public SolidColorBrush SwatchBrush { get; } = new(HdrColor.Transparent);

	/// <summary>The pixel lens: a block per pixel round the pointer, drawn beside it.</summary>
	public Border Lens { get; }

	/// <summary>Draw the lens at <paramref name="at"/>, in surface pixels, with <paramref name="samples"/> by
	/// <paramref name="samples"/> blocks of <paramref name="factor"/> pixels each, coloured from <paramref name="rgba"/>; hide
	/// it for a null position.</summary>
	public void ShowLens(Vector2? at, int samples, int factor, ReadOnlySpan<byte> rgba)
	{
		if (at is not { } position)
		{
			Lens.Visibility = Visibility.Collapsed;
			return;
		}

		// Each block keeps one brush and is recoloured through it, so a capture makes no brush per pixel.
		var blocks = (Canvas)Lens.Child!;
		while (blocks.Children.Count < samples * samples)
			blocks.Children.Add(new Border { Background = new SolidColorBrush(HdrColor.Transparent) });
		var size = factor / Scale;
		for (var i = 0; i < samples * samples; i++)
		{
			var block = (Border)blocks.Children[i];
			block.Width = size;
			block.Height = size;
			Canvas.SetLeft(block, i % samples * size);
			Canvas.SetTop(block, i / samples * size);
			((SolidColorBrush)block.Background!).Color = HdrColor.FromRgb(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
		}
		blocks.Width = blocks.Height = samples * size;
		Canvas.SetLeft(Lens, position.X / Scale);
		Canvas.SetTop(Lens, position.Y / Scale);
		Lens.Visibility = Visibility.Visible;
	}


	/// <summary>The view's DIP-to-pixel scale.</summary>
	public float Scale => View.Root.LayoutScale > 0f ? View.Root.LayoutScale : 1f;

	/// <summary>Paint a toolbar button: lit, at rest, or unavailable.</summary>
	public void Paint(string button, bool lit, bool enabled = true)
	{
		if (_buttons.TryGetValue(button, out var border))
			border.Background = !enabled ? _disabled : lit ? _accent : _idle;
	}

	/// <summary>Show <paramref name="box"/> over <paramref name="pixels"/>, a rectangle in surface pixels, or hide it.</summary>
	public void Place(Border box, Rect? pixels)
	{
		if (pixels is not { } rect)
		{
			box.Visibility = Visibility.Collapsed;
			return;
		}
		Canvas.SetLeft(box, rect.X / Scale);
		Canvas.SetTop(box, rect.Y / Scale);
		box.Width = MathF.Max(rect.Width / Scale, 1f);
		box.Height = MathF.Max(rect.Height / Scale, 1f);
		box.Visibility = Visibility.Visible;
	}

	/// <summary>Show a badge with <paramref name="caption"/> above <paramref name="pixels"/>, or inside it where above
	/// would leave the surface; hide it for a null rectangle.</summary>
	public void Label(Border badge, Rect? pixels, string caption)
	{
		if (pixels is not { } rect)
		{
			badge.Visibility = Visibility.Collapsed;
			return;
		}
		((TextBlock)badge.Child!).Text = caption;
		var top = rect.Y / Scale - 18f;
		Canvas.SetLeft(badge, rect.X / Scale);
		Canvas.SetTop(badge, top < 0f ? rect.Y / Scale + 2f : top);
		badge.Visibility = Visibility.Visible;
	}

	/// <summary>Show a badge with <paramref name="caption"/> at a point in surface pixels.</summary>
	public void LabelAt(Border badge, float x, float y, string caption)
	{
		((TextBlock)badge.Child!).Text = caption;
		Canvas.SetLeft(badge, x / Scale);
		Canvas.SetTop(badge, y / Scale);
		badge.Visibility = Visibility.Visible;
	}

	/// <summary>The change marks, outline and tag, growing the pool to <paramref name="count"/> and hiding the rest.</summary>
	public IReadOnlyList<(Border Box, Border Tag)> ChangeMarks(int count)
	{
		while (_changeMarks.Count < count)
			_changeMarks.Add((Outline(_change, 1f), Badge(_change)));
		for (var i = count; i < _changeMarks.Count; i++)
		{
			_changeMarks[i].Box.Visibility = Visibility.Collapsed;
			_changeMarks[i].Tag.Visibility = Visibility.Collapsed;
		}
		return _changeMarks;
	}

	private Border Outline(Brush colour, float thickness)
	{
		var box = new Border { BorderBrush = colour, BorderThickness = new Thickness(thickness), Visibility = Visibility.Collapsed };
		Marks.Children.Add(box);
		return box;
	}

	private Border Line()
	{
		var line = new Border { Background = _ruler, Visibility = Visibility.Collapsed };
		Marks.Children.Add(line);
		return line;
	}

	private Border Badge(Brush colour)
	{
		var badge = new Border
		{
			Background = colour,
			Padding = new Thickness(3f, 1f),
			Child = new TextBlock { FontSize = 11f, Foreground = _badgeInk },
			Visibility = Visibility.Collapsed,
		};
		Marks.Children.Add(badge);
		return badge;
	}

	private TextBlock Label(string text, float size) => new() { Text = text, FontSize = size, Foreground = _ink, Margin = new Thickness(4f, 2f) };

	private Border Button(string name, Action action)
	{
		var button = new Border
		{
			Background = _idle,
			Padding = new Thickness(6f, 3f),
			Margin = new Thickness(1f),
			Child = Label(name, 11f),
		};
		button.AddHandler(UIElement.PointerPressedEvent, (_, e) =>
		{
			e.Handled = true;
			action();
		});
		return button;
	}
}
