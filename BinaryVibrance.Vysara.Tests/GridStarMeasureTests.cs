using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// A Grid measures a child in a Star track against that track's share of the extent it is offered, as WinUI does, so
/// what fits itself to a width (wrapped text) fits the grid's; beside an Auto track, a Star track's share is what the
/// Auto tracks leave, measured once they are sized, as WinUI measures star cells after auto ones.
/// </summary>
public sealed class GridStarMeasureTests
{
	private const float FontSize = 19f;
	private const string Long = "Copy a link to join the event in a browser";

	private static TextBlock Wrapped() => new() { Text = Long, FontSize = FontSize, TextWrapping = TextWrapping.Wrap };

	private static void Lay(UIElement content, float width)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(width, 600));
	}

	[Test]
	public void Text_in_a_plain_grid_wraps_at_the_grids_width()
	{
		var text = Wrapped();
		var grid = new Grid { Width = 200 };
		grid.Children.Add(text);
		// A horizontal stack offers its children unlimited width; the grid's own Width bounds its star column
		var frame = new StackPanel { Orientation = Orientation.Horizontal };
		frame.Children.Add(grid);
		Lay(frame, 1280);
		text.DesiredSize.Width.ShouldBeLessThanOrEqualTo(200f);
		text.DesiredSize.Height.ShouldBeGreaterThan(FontAtlas.Default.LineHeight * FontSize * 1.5f);
	}

	[Test]
	public void Star_columns_measure_at_their_share_after_pixel_columns()
	{
		var left = Wrapped();
		var right = Wrapped();
		var grid = new Grid { Width = 500 };
		grid.ColumnDefinitions.Add(new GridLength(100, GridUnitType.Pixel));
		grid.ColumnDefinitions.Add(new GridLength(1, GridUnitType.Star));
		grid.ColumnDefinitions.Add(new GridLength(3, GridUnitType.Star));
		Grid.SetColumn(left, 1);
		Grid.SetColumn(right, 2);
		grid.Children.Add(left);
		grid.Children.Add(right);
		Lay(grid, 1280);
		left.DesiredSize.Width.ShouldBeLessThanOrEqualTo(100f + 0.01f);    // a quarter of the 400 left
		right.DesiredSize.Width.ShouldBeLessThanOrEqualTo(300f + 0.01f);   // three quarters
	}

	[Test]
	public void A_star_column_beside_an_auto_column_measures_at_what_the_auto_column_leaves()
	{
		var text = Wrapped();
		var label = new Border { Width = 50, Height = 10 };
		var grid = new Grid { Width = 200 };
		grid.ColumnDefinitions.Add(GridLength.Auto);
		grid.ColumnDefinitions.Add(new GridLength(1, GridUnitType.Star));
		Grid.SetColumn(text, 1);
		grid.Children.Add(label);
		grid.Children.Add(text);
		Lay(grid, 1280);
		text.DesiredSize.Width.ShouldBeLessThanOrEqualTo(150f + 0.01f); // the 200 less the Auto column's 50
		text.DesiredSize.Height.ShouldBeGreaterThan(FontAtlas.Default.LineHeight * FontSize * 1.5f);
	}
}
