using SUIsei.Controls;
using SUIsei.Drawing;

namespace SUIsei.Tests;

/// <summary>
/// MU2 layout completion (PlanningUI.md §D2.3 / §D8): Grid row/column span (a child's cell sums the tracks it spans)
/// and <see cref="ContentPresenter"/> content hosting (a string → a <see cref="TextBlock"/>; a UIElement directly).
/// </summary>
public sealed class LayoutMU2Tests
{
	private static void Layout(UIElement content, float w = 300f, float h = 100f)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(w, h));
	}

	[Test]
	public void Grid_column_span_sums_the_spanned_tracks()
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(GridLength.Pixels(100));
		grid.ColumnDefinitions.Add(GridLength.Pixels(50));
		grid.RowDefinitions.Add(GridLength.Pixels(40));
		var cell = new Border();
		Grid.SetColumn(cell, 0);
		Grid.SetColumnSpan(cell, 2);
		grid.Children.Add(cell);
		Layout(grid);

		cell.LayoutRect.ShouldBe(new Rect(0, 0, 150, 40)); // 100 + 50 wide
	}

	[Test]
	public void Grid_row_span_sums_the_spanned_rows()
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(GridLength.Pixels(60));
		grid.RowDefinitions.Add(GridLength.Pixels(30));
		grid.RowDefinitions.Add(GridLength.Pixels(20));
		var cell = new Border();
		Grid.SetRow(cell, 0);
		Grid.SetRowSpan(cell, 2);
		grid.Children.Add(cell);
		Layout(grid);

		cell.LayoutRect.ShouldBe(new Rect(0, 0, 60, 50)); // 30 + 20 tall
	}

	[Test]
	public void ContentPresenter_wraps_a_string_in_a_textblock()
	{
		var cp = new ContentPresenter { Content = "Hello" };
		Layout(cp);

		cp.VisualChildren.Count.ShouldBe(1);
		cp.VisualChildren[0].ShouldBeOfType<TextBlock>();
	}

	[Test]
	public void ContentPresenter_hosts_a_uielement_directly()
	{
		var inner = new Border { Width = 20f, Height = 10f };
		var cp = new ContentPresenter { Content = inner };
		Layout(cp);

		cp.VisualChildren[0].ShouldBe(inner);
	}

	[Test]
	public void ContentControl_keeps_its_hosted_child_when_an_equal_content_is_set_again()
	{
		var cc = new ContentControl { Content = new string('x', 3) };
		var hosted = cc.VisualChildren[0];

		cc.Content = new string('x', 3); // a different string instance with an equal value
		cc.VisualChildren[0].ShouldBeSameAs(hosted); // value-equality short-circuit — the wrapping TextBlock is kept
	}
}
