using System.Linq;
using Vysara.Controls;
using Vysara.Drawing;
using Vysara.Extensions;

namespace Vysara.Tests;

/// <summary>
/// UI virtualization coverage: a <see cref="VirtualizingStackPanel"/> realizes only the
/// containers intersecting its viewport and recycles the rest as the offset changes, so a large list never
/// materializes all its containers. Fixed row height; offset set directly. Headless — realize/recycle counters.
/// </summary>
public sealed class VirtualizationTests
{
	private static (ItemsControl Control, VirtualizingStackPanel Panel, UIRoot Root) BigList(int count, float rowHeight, float viewportHeight)
	{
		var ic = new ItemsControl
		{
			ItemsPanel = new ItemsPanelTemplate
			{
				Factory = () =>
				{
					var panel = new VirtualizingStackPanel();
					Virtualization.SetRowHeight(panel, rowHeight);
					return panel;
				},
			},
			ItemsSource = Enumerable.Range(0, count).Select(i => $"item{i}").ToArray(),
		};
		var root = new UIRoot(new DrawListId(0)) { Content = ic };
		root.LayoutPass(new Size(200, viewportHeight));
		return (ic, (VirtualizingStackPanel)ic.ItemsHost!, root);
	}

	[Test]
	public void Only_the_visible_window_is_realized_for_a_large_list()
	{
		var (ic, panel, _) = BigList(count: 100, rowHeight: 20f, viewportHeight: 100f);

		((IItemContainerSource)ic).ItemCount.ShouldBe(100);
		panel.VisualChildren.Count.ShouldBe(6); // ceil(100/20) + 1 partial row
		ic.RealizeCount.ShouldBe(6);
		ic.RecycleCount.ShouldBe(0);
		panel.ExtentHeight.ShouldBe(2000f); // full content height for an outer scroller
	}

	[Test]
	public void Scrolling_one_row_recycles_one_and_realizes_one()
	{
		var (ic, panel, root) = BigList(count: 100, rowHeight: 20f, viewportHeight: 100f);

		panel.VerticalOffset = 20f; // scroll down one row
		root.LayoutPass(new Size(200, 100));

		ic.RecycleCount.ShouldBe(1);
		ic.RealizeCount.ShouldBe(7);
		panel.VisualChildren.Count.ShouldBe(6); // window size unchanged
												// The window is now items [1..6]; the recycled container was rebound to the newly-visible item.
		((TextBlock)panel.VisualChildren[5]).Text.ShouldBe("item6");
	}

	[Test]
	public void Realized_containers_are_positioned_at_their_index_offset()
	{
		var (_, panel, _) = BigList(count: 50, rowHeight: 25f, viewportHeight: 100f);

		// First realized container (index 0) sits at the top; the second a row below it.
		panel.VisualChildren[0].LayoutRect.Y.ShouldBe(0f, 0.01f);
		panel.VisualChildren[1].LayoutRect.Y.ShouldBe(25f, 0.01f);
	}
}
