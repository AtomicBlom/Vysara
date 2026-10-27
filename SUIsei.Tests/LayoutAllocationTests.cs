using System.Collections.ObjectModel;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Extensions;

namespace SUIsei.Tests;

/// <summary>
/// The layout pass allocates nothing once warm, in every panel and presenter: a full re-measure and re-arrange of the
/// tree, a width animated on a child, and a margin animated on one, each relaying the tree out every frame, allocate no
/// managed memory. The layout properties are animatable, so a storyboard on one relays out every frame it runs.
/// </summary>
public sealed class LayoutAllocationTests
{
	private static readonly Size Surface = new(400f, 600f);
	private const double Frame = 1.0 / 60.0;

	// A margin has no standard interpolation, so its tween is given one.
	private static readonly Lerp<Thickness> ThicknessLerp = static (a, b, t) => new Thickness(
		a.Left + (b.Left - a.Left) * t, a.Top + (b.Top - a.Top) * t, a.Right + (b.Right - a.Right) * t, a.Bottom + (b.Bottom - a.Bottom) * t);

	/// <summary>A tree to lay out, and the element in it whose width or margin is animated.</summary>
	private sealed record Scene(FrameworkElement Content, FrameworkElement Animated);

	public static IEnumerable<string> Scenes() =>
	[
		"Grid", "Grid with no definitions", "StackPanel", "Canvas", "Border", "ContentPresenter", "Button", "ItemsControl",
		"VirtualizingStackPanel", "ScrollViewer",
	];

	private static Scene Build(string scene)
	{
		switch (scene)
		{
			case "Grid":
				{
					// Pixel, Auto and Star tracks both ways, cells that span both, and text in an Auto cell. The animated child
					// sits alone in an Auto column, so its width resizes that column and the Star columns share what is left.
					var grid = new Grid
					{
						ColumnDefinitions = { GridLength.Pixels(60f), GridLength.Auto, GridLength.Star, GridLength.Stars(2f) },
						RowDefinitions = { GridLength.Auto, GridLength.Pixels(30f), GridLength.Star, GridLength.Stars(3f) },
					};
					var animated = Box(width: 80f, height: 20f);
					Place(grid, animated, row: 0, column: 1);
					Place(grid, Box(height: 12f), row: 0, column: 0);
					Place(grid, new TextBlock { Text = "Auto row" }, row: 0, column: 2);
					Place(grid, Box(), row: 1, column: 0, columnSpan: 3);
					Place(grid, Box(), row: 1, column: 3, rowSpan: 2);
					Place(grid, Box(width: 30f), row: 2, column: 1);
					Place(grid, Box(), row: 2, column: 2, rowSpan: 2, columnSpan: 2);
					Place(grid, Box(), row: 3, column: 0, columnSpan: 2);
					Place(grid, Box(), row: 9, column: 9); // out of range: clamped to the last cell
					return new Scene(grid, animated);
				}

			case "Grid with no definitions":
				{
					var grid = new Grid();
					var animated = Box(width: 80f, height: 20f);
					grid.Children.Add(animated);
					grid.Children.Add(Box(height: 40f));
					grid.Children.Add(new TextBlock { Text = "Over the rest" });
					return new Scene(grid, animated);
				}

			case "StackPanel":
				{
					var stack = new StackPanel();
					var animated = Box(width: 80f, height: 20f);
					stack.Children.Add(animated);
					stack.Children.Add(new TextBlock { Text = "A line of text" });
					var row = new StackPanel { Orientation = Orientation.Horizontal };
					for (var i = 0; i < 4; i++)
						row.Children.Add(Box(width: 20f + i, height: 10f));
					stack.Children.Add(row);
					stack.Children.Add(Box(height: 16f));
					return new Scene(stack, animated);
				}

			case "Canvas":
				{
					var canvas = new Canvas();
					var animated = Box(width: 80f, height: 20f);
					canvas.Children.Add(animated);
					for (var i = 0; i < 4; i++)
					{
						var box = Box(width: 10f, height: 10f);
						Canvas.SetLeft(box, 20f * i);
						Canvas.SetTop(box, 30f + 5f * i);
						canvas.Children.Add(box);
					}
					var text = new TextBlock { Text = "Placed" };
					Canvas.SetLeft(text, 100f);
					canvas.Children.Add(text);
					return new Scene(canvas, animated);
				}

			case "Border":
				{
					var animated = Box(width: 80f, height: 20f);
					var inner = new Border { Padding = new Thickness(2f, 3f, 4f, 5f), BorderThickness = new Thickness(1f), Child = animated };
					var outer = new Border
					{
						Padding = new Thickness(6f),
						BorderThickness = new Thickness(2f),
						HorizontalAlignment = HorizontalAlignment.Left,
						Child = inner,
					};
					return new Scene(outer, animated);
				}

			case "ContentPresenter":
				{
					// One presenter hosting an element, beside one wrapping a string in the text it makes for it.
					var animated = Box(width: 80f, height: 20f);
					var stack = new StackPanel();
					stack.Children.Add(new ContentPresenter { Content = animated });
					stack.Children.Add(new ContentPresenter { Content = "Wrapped text" });
					return new Scene(stack, animated);
				}

			case "Button":
				{
					// A templated control: its built-in template's border and content presenter, around string content.
					var button = new Button { Content = "Press", HorizontalAlignment = HorizontalAlignment.Left };
					var stack = new StackPanel();
					stack.Children.Add(button);
					stack.Children.Add(new ContentControl { Content = Box(height: 10f) });
					return new Scene(stack, button);
				}

			case "ItemsControl":
				{
					var items = new ItemsControl { ItemsSource = new ObservableCollection<string>(Enumerable.Range(0, 12).Select(static i => $"Item {i}")) };
					return new Scene(items, items);
				}

			case "VirtualizingStackPanel":
				{
					var items = new ItemsControl
					{
						Height = 200f,
						ItemsPanel = new ItemsPanelTemplate { Factory = static () => new VirtualizingStackPanel() },
						ItemsSource = Enumerable.Range(0, 1000).Select(static i => $"Row {i}").ToList(),
					};
					return new Scene(items, items);
				}

			case "ScrollViewer":
				{
					var stack = new StackPanel();
					var animated = Box(width: 80f, height: 20f);
					stack.Children.Add(animated);
					for (var i = 0; i < 40; i++)
						stack.Children.Add(i % 2 == 0 ? Box(height: 24f) : new TextBlock { Text = "Scrolled text" });
					var scroller = new ScrollViewer { Height = 300f, Content = stack };
					scroller.ScrollToVerticalOffset(0f);
					return new Scene(scroller, animated);
				}

			default:
				throw new ArgumentOutOfRangeException(nameof(scene), scene, null);
		}
	}

	private static Border Box(float width = float.NaN, float height = float.NaN) => new() { Width = width, Height = height };

	private static void Place(Grid grid, UIElement child, int row, int column, int rowSpan = 1, int columnSpan = 1)
	{
		Grid.SetRow(child, row);
		Grid.SetColumn(child, column);
		Grid.SetRowSpan(child, rowSpan);
		Grid.SetColumnSpan(child, columnSpan);
		grid.Children.Add(child);
	}

	private static UIRoot Root(UIElement content)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(Surface);
		return root;
	}

	// --- Every element re-measured and re-arranged ---

	[Test]
	[MethodDataSource(nameof(Scenes))]
	public void A_forced_relayout_allocates_nothing_once_warm(string scene)
	{
		var (content, _) = Build(scene);
		var root = Root(content);
		void Passes(int count)
		{
			for (var i = 0; i < count; i++)
			{
				content.InvalidateMeasureTree();
				root.LayoutPass(Surface);
			}
		}

		Passes(3);
		content.MeasureDirty.ShouldBeFalse();
		Allocations.ShouldAllocateNothing(() => Passes(30));
	}

	[Test]
	[MethodDataSource(nameof(Scenes))]
	public void A_relayout_into_a_resized_surface_allocates_nothing_once_warm(string scene)
	{
		var (content, _) = Build(scene);
		var root = Root(content);
		var i = 0;
		void Passes(int count)
		{
			for (var n = 0; n < count; n++, i++)
				root.LayoutPass(new Size(Surface.Width - i % 50, Surface.Height - i % 70));
		}

		Passes(60);
		Allocations.ShouldAllocateNothing(() => Passes(30));
	}

	// --- A layout property animated, relaying the tree out every frame ---

	[Test]
	[MethodDataSource(nameof(Scenes))]
	public void An_animated_width_relays_out_without_allocating_once_warm(string scene)
	{
		var (content, animated) = Build(scene);
		var root = Root(content);
		var clock = new AnimationClock();
		var storyboard = new Storyboard();
		storyboard.Children.Add(new DoubleAnimation
		{
			From = 50.0,
			To = 250.0,
			Duration = TimeSpan.FromSeconds(1),
			AutoReverse = true,
			RepeatBehavior = RepeatBehavior.Forever,
			Target = animated,
			TargetProperty = FrameworkElement.WidthProperty,
		});
		clock.Begin(storyboard);
		var widths = new HashSet<float>();
		void Frames(int count)
		{
			for (var f = 0; f < count; f++)
			{
				clock.Advance(Frame);
				root.LayoutPass(Surface);
			}
		}

		Frames(30);
		for (var f = 0; f < 3; f++, Frames(1))
			widths.Add(animated.LayoutRect.Width);
		widths.Count.ShouldBe(3); // it does relay out every frame
		Allocations.ShouldAllocateNothing(() => Frames(60));
	}

	[Test]
	[MethodDataSource(nameof(Scenes))]
	public void An_animated_margin_relays_out_without_allocating_once_warm(string scene)
	{
		var (content, animated) = Build(scene);
		var root = Root(content);
		var clock = new AnimationClock();
		clock.Tween(animated, static e => e.Margin, static (e, v) => e.Margin = v, new Thickness(400f, 300f, 200f, 100f),
			TimeSpan.FromSeconds(1000), lerp: ThicknessLerp);
		var placed = new HashSet<Rect>();
		void Frames(int count)
		{
			for (var f = 0; f < count; f++)
			{
				clock.Advance(Frame);
				root.LayoutPass(Surface);
			}
		}

		Frames(30);
		for (var f = 0; f < 3; f++, Frames(1))
			placed.Add(animated.LayoutRect);
		placed.Count.ShouldBe(3);
		Allocations.ShouldAllocateNothing(() => Frames(60));
	}

	// --- Scrolling: an arrange every frame, and a virtualizing panel recycling rows ---

	[Test]
	public void Scrolling_a_scroll_viewer_allocates_nothing_once_warm()
	{
		var (content, _) = Build("ScrollViewer");
		var scroller = (ScrollViewer)content;
		var root = Root(scroller);
		var offset = 0f;
		void Frames(int count)
		{
			for (var f = 0; f < count; f++)
			{
				offset = (offset + 7f) % scroller.ScrollableHeight;
				scroller.ScrollToVerticalOffset(offset);
				root.LayoutPass(Surface);
			}
		}

		Frames(30);
		Allocations.ShouldAllocateNothing(() => Frames(60));
	}

	[Test]
	public void Scrolling_a_virtualizing_panel_allocates_nothing_once_warm()
	{
		var (content, _) = Build("VirtualizingStackPanel");
		var panel = (VirtualizingStackPanel)((ItemsControl)content).ItemsHost!;
		var root = Root(content);
		var rowHeight = Virtualization.GetRowHeight(panel);
		var offset = 0f;
		void Frames(int count)
		{
			for (var f = 0; f < count; f++)
			{
				offset += rowHeight * 0.4f; // a row recycled every few frames, and another realized from the pool
				panel.VerticalOffset = offset;
				root.LayoutPass(Surface);
			}
		}

		Frames(30);
		Allocations.ShouldAllocateNothing(() => Frames(60));
	}
}
